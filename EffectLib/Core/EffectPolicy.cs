using System;

namespace EffectLib
{
    public static class EffectCapability
    {
        public const string Fly = "fly";

        public const string Climb = "climb";

        public const string Fall = "fall";

        public const string Refresh = "refresh";

        public const string Resize = "resize";
    }

    public static class EffectPolicy
    {
        private static Func<string, bool> gate;

        public static void SetGate(Func<string, bool> allow) => gate = allow;

        public static bool IsAllowed(string capability)
        {
            Func<string, bool> allow = gate;
            if (allow == null)
                return DefaultIsAllowed(capability);

            try
            {
                return allow(capability);
            }
            catch
            {
                return true;
            }
        }

        private static bool DefaultIsAllowed(string capability) =>
            capability switch
            {
                EffectCapability.Fly => EffectLibConfig.Loaded.AllowFly,
                EffectCapability.Climb => EffectLibConfig.Loaded.AllowClimb,
                EffectCapability.Fall => EffectLibConfig.Loaded.AllowFall,
                EffectCapability.Refresh => EffectLibConfig.Loaded.AllowRefresh,
                EffectCapability.Resize => EffectLibConfig.Loaded.AllowResize,
                _ => true,
            };
    }
}
