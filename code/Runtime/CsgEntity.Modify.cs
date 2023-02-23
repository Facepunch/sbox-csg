#if !SANDBOX_EDITOR

using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Sandbox.Event;

namespace Sandbox.Csg
{
    partial class CsgEntity
    {
        private const int MaxModificationsPerMessage = 8;
        private const int SendModificationsRpc = 269924031; // CsgSolid.SendModifications

        private int _appliedModifications;
        private readonly List<CsgModification> _modifications = new List<CsgModification>();
        private readonly Dictionary<Entity, int> _sentModifications = new Dictionary<Entity, int>();

        [ThreadStatic]
        private static List<Entity> _sToRemove;

        private void AddModification( in CsgModification modification )
        {
            _modifications.Add( modification );
        }

        private void SendModifications()
        {
            _sToRemove ??= new List<Entity>();
            _sToRemove.Clear();

            foreach ( var (pawn, _) in _sentModifications )
            {
                if ( !pawn.IsValid )
                {
                    _sToRemove.Add( pawn );
                }
            }

            foreach ( var entity in _sToRemove )
            {
                _sentModifications.Remove( entity );
            }

            foreach ( var client in Game.Clients )
            {
                if ( client.IsBot ) continue;
                if ( client.Pawn is not Entity pawn ) continue;

                if ( !_sentModifications.TryGetValue( pawn, out var prevCount ) )
                {
                    prevCount = 0;
                    _sentModifications.Add( pawn, prevCount );
                }

                Assert.True( prevCount <= _modifications.Count );

                if ( prevCount == _modifications.Count ) continue;

                var msg = NetWrite.StartRpc( SendModificationsRpc, this );

                var msgCount = Math.Min( _modifications.Count - prevCount, MaxModificationsPerMessage );

                msg.Write( prevCount );
                msg.Write( msgCount );
                msg.Write( _modifications.Count );

                for ( var i = 0; i < msgCount; i++ )
                {
                    WriteModification( msg, _modifications[prevCount + i] );
                }

                msg.SendRpc( To.Single( client ), null );

                _sentModifications[pawn] = prevCount + msgCount;
            }
        }

        private void ReceiveModifications( ref NetRead read )
        {
            var prevCount = read.Read<int>();
            var msgCount = read.Read<int>();
            var totalCount = read.Read<int>();

            CsgHelpers.AssertAreEqual( prevCount, _modifications.Count );

            for ( var i = 0; i < msgCount; ++i )
            {
                _modifications.Add( ReadModification( ref read ) );
            }

            OnModificationsChanged();
        }

        private static void WriteModification( NetWrite writer, in CsgModification value )
        {
            // Write operator

            writer.Write( value.Operator );

            // Write brush

            switch ( value.Operator )
            {
                case CsgOperator.Disconnect:
                    break;

                default:
                    Assert.NotNull( value.Brush );
                    value.Brush.Serialize( writer );
                    break;
            }

            // Write material

            switch ( value.Operator )
            {
                case CsgOperator.Disconnect:
                case CsgOperator.Subtract:
                    break;

                default:
                    Assert.NotNull( value.Material );
                    value.Material.Serialize( writer );
                    break;
            }

            // Write transform

            switch ( value.Operator )
            {
                case CsgOperator.Disconnect:
                    break;

                default:
                    writer.Write( value.Transform.HasValue );

                    if ( value.Transform.HasValue )
                    {
                        writer.Write( value.Transform.Value );
                    }
                    break;
            }
        }

        private static CsgModification ReadModification( ref NetRead reader )
        {
            CsgBrush brush = null;
            CsgMaterial material = null;
            Matrix? transform = null;

            // Read operator

            var op = reader.Read<CsgOperator>();

            // Read brush

            switch ( op )
            {
                case CsgOperator.Disconnect:
                    break;

                default:
                    brush = CsgBrush.Deserialize( ref reader );
                    break;
            }

            // Read material

            switch ( op )
            {
                case CsgOperator.Disconnect:
                case CsgOperator.Subtract:
                    break;

                default:
                    material = CsgMaterial.Deserialize( ref reader );
                    break;
            }

            // Read transform

            switch ( op )
            {
                case CsgOperator.Disconnect:
                    break;

                default:
                    if ( reader.Read<bool>() )
                    {
                        transform = reader.Read<Matrix>();
                    }
                    break;
            }

            return new CsgModification( op, brush, material, transform );
        }

        protected override void OnCallRemoteProcedure( int id, NetRead read )
        {
            switch ( id )
            {
                case SendModificationsRpc:
                    ReceiveModifications( ref read );
                    break;

                default:
                    base.OnCallRemoteProcedure( id, read );
                    break;
            }
        }

        private void OnModificationsChanged()
        {
            if ( _grid == null ) return;

            if ( ServerDisconnectedFrom != null && !_copiedInitialGeometry )
            {
                return;
            }

            while ( _appliedModifications < _modifications.Count )
            {
                ModifyInternal( _modifications[_appliedModifications++] );
            }
        }

        private Matrix WorldToLocal => Matrix.CreateTranslation( -Position )
            * Matrix.CreateScale( 1f / Scale )
            * Matrix.CreateRotation( Rotation.Inverse );

        private bool Modify( CsgOperator op, CsgBrush brush, CsgMaterial material, in Matrix? transform )
        {
            Game.AssertServer( nameof( Modify ) );

            var mod = new CsgModification( op, brush, material, transform.HasValue ? transform.Value * WorldToLocal : null );

            if ( ModifyInternal( mod ) )
            {
                AddModification( mod );
                return true;
            }

            return false;
        }

        private bool ModifyInternal( in CsgModification modification )
        {
            if ( Deleted )
            {
                if ( Game.IsServer )
                {
                    Log.Warning( $"Attempting to modify a deleted {nameof( CsgSolid )}" );
                }

                return false;
            }

            Timer.Restart();

            var hulls = CsgHelpers.RentHullList();

            try
            {
                if ( modification.Operator == CsgOperator.Disconnect )
                {
                    return ConnectivityUpdate();
                }

                modification.Brush.CreateHulls( hulls );

                var changed = false;

                foreach ( var solid in hulls )
                {
                    solid.Material = modification.Material;

                    if ( modification.Transform.HasValue )
                    {
                        solid.Transform( modification.Transform.Value );
                    }
                }

                foreach ( var solid in hulls )
                {
                    changed |= Solid.Modify( solid, modification.Operator );
                }

                return changed;
            }
            finally
            {
                CsgHelpers.Return( hulls );

                if ( LogTimings )
                {
                    Log.Info( $"Modify {modification.Operator}: {Timer.Elapsed.TotalMilliseconds:F2}ms" );
                }
            }
        }

        public bool Add( CsgBrush brush, CsgMaterial material,
            Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            Assert.NotNull( material );

            return Modify( CsgOperator.Add, brush, material, CreateMatrix( position, scale, rotation ) );
        }

        public bool Subtract( CsgBrush brush,
            Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            return Modify( CsgOperator.Subtract, brush, null, CreateMatrix( position, scale, rotation ) );
        }

        public bool Replace( CsgBrush brush, CsgMaterial material,
            Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            Assert.NotNull( material );

            return Modify( CsgOperator.Replace, brush, material, CreateMatrix( position, scale, rotation ) );
        }

        public bool Paint( CsgBrush brush, CsgMaterial material,
            Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            Assert.NotNull( material );

            return Modify( CsgOperator.Paint, brush, material, CreateMatrix( position, scale, rotation ) );
        }

        private bool Disconnect()
        {
            return Modify( CsgOperator.Disconnect, null, null, default );
        }
    }
}

#endif
