namespace UnityAV
{
    internal static class NativePlugin
    {
#if UNITY_IOS && !UNITY_EDITOR
        public const string Name = "__Internal";
#else
        public const string Name = "rustav_native";
#endif
    }
}
