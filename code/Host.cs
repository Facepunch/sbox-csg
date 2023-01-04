internal static class Host
{
    public static string Name
    {
        get
        {
            if( IsServer )
            {
                return "Server";
            }
            return SteamId.ToString();
        }
    }
}
