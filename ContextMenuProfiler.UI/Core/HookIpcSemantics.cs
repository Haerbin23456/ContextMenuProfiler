namespace ContextMenuProfiler.UI.Core
{
    public static class HookIpcSemantics
    {
        public static class Protocol
        {
            public const string VersionPrefix = "CMP1";
            public const string ModeAuto = "AUTO";
            public const char FieldDelimiter = '|';
        }

        public static class Runtime
        {
            public const string PipeName = "ContextMenuProfilerHook";
            public const int ConnectTimeoutMs = 1200;
            public const int RoundTripTimeoutMs = 2000;
            public const int MaxConcurrentCalls = 3;
            public const int MaxAttempts = 2;
            public const int RetryDelayMs = 80;
            public const string ProbeFileName = "ContextMenuProfiler_probe.txt";
            public const string ProbeFileContent = "probe";
            public const int InitialResponseCapacity = 1024;
            public const int ReadChunkSize = 4096;
            public const int FrameHeaderBytes = 4;
            public const int MaxRequestBytes = 16384;
            public const int MaxResponseBytes = 65536;
        }

        public static class Response
        {
            public const char MultiValueDelimiter = '|';
            public const string NoIconToken = "NONE";
        }
    }
}
