#if !SANDBOX_EDITOR

using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Sandbox.Csg
{
    partial class CsgEntity
    {
        private bool _firstCollisionUpdate = true;

        private bool CollisionUpdate()
        {
            if ( _grid.Count == 0 ) return false;

            var newBody = _firstCollisionUpdate;
            _firstCollisionUpdate = false;

            if ( !PhysicsBody.IsValid() )
            {
                if ( LogTimings )
                {
                    Log.Info( $"Creating physics body for {Name}" );
                }

                SetupPhysicsFromSphere( IsStatic ? PhysicsMotionType.Static : PhysicsMotionType.Dynamic, 0f, 1f );

                Assert.True( PhysicsBody.IsValid() );

                newBody = true;
            }

            if ( newBody )
            {
                PhysicsBody.ClearShapes();

                foreach ( var (_, cell) in _grid )
                {
                    cell.InvalidateCollision();
                }
            }

            if ( _invalidCollision.Count == 0 )
            {
                return false;
            }

            Timer.Restart();

            var body = PhysicsBody;

            var changedColliders = 0;

            foreach ( var cell in _invalidCollision )
            {
                if ( cell.Solid != this ) continue;

                var mass = 0f;
                var volume = 0f;

                foreach ( var hull in cell.Hulls )
                {
                    if ( newBody )
                    {
                        hull.RemoveCollider();
                    }

                    if ( hull.UpdateCollider( body ) )
                    {
                        changedColliders++;
                    }

                    mass += hull.Volume * hull.Material.Density;
                    volume += hull.Volume;
                }

                cell.Mass = mass;
                cell.Volume = volume;

                cell.PostCollisionUpdate();
            }

            _invalidCollision.Clear();

            var totalVolume = 0f;
            var totalMass = 0f;

            foreach ( var (_, cell) in _grid )
            {
                totalMass += cell.Mass;
                totalVolume += cell.Volume;
            }

            Volume = totalVolume;

            if ( !IsStatic )
            {
                body.Mass = totalMass;
                body.RebuildMass();
                body.Sleeping = false;
            }

            if ( LogTimings )
            {
                Log.Info( $"Collision update: {Timer.Elapsed.TotalMilliseconds:F2}ms {changedColliders} of {PhysicsBody.Shapes.Count()}" );
            }

            return true;
        }

    }
}

#endif
