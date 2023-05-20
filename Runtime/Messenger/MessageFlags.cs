using System;

namespace NativeMessenger
{
    [Flags]
    public enum MessageFlags : byte
    {
        None = 0,
        Multi = 1 << 1,
    }
}