using Godot;

public static class RuntimeMode
{
    public static bool IsServer
    {
        get
        {
            bool hasClientFlag = false;
            foreach (string arg in OS.GetCmdlineArgs())
            {
                if (arg == "--server") return true;
                if (arg == "--client") hasClientFlag = true;
            }

            if (hasClientFlag) return false;
            return DisplayServer.GetName() == "headless";
        }
    }

    public static bool IsClient => !IsServer;

    public static bool TryGetServerPort(out int port)
    {
        foreach (string arg in OS.GetCmdlineArgs())
        {
            if (!arg.StartsWith("--port=")) continue;

            string portValue = arg.Substring("--port=".Length);
            if (int.TryParse(portValue, out int parsedPort) && parsedPort > 0 && parsedPort <= 65535)
            {
                port = parsedPort;
                return true;
            }
        }

        port = 0;
        return false;
    }
}