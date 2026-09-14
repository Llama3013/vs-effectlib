using System.Linq;
using System.Reflection;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    public class EffectLibConfig
    {
        public static EffectLibConfig Loaded { get; set; } = new();

        private static readonly PropertyInfo[] SyncProps =
        [
            .. typeof(EffectLibConfig)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite),
        ];

        // Hard floor/ceiling for any size-changing effect - never exceeded even if an effect's
        // own JSON/config asks for a wider range.
        public float MinPlayerHeight { get; set; } = UtilityEffects.DefaultMinHeight;
        public float MaxPlayerHeight { get; set; } = UtilityEffects.DefaultMaxHeight;

        // Weapons are matched by vanilla item/block tags (comma-separated); arrows as of VS 1.22.7 have no
        // reliable tag of their own, so projectiles are matched by wildcard item code instead.
        public string CoatableWeaponTags { get; set; } = "weapon-melee";
        public string CoatableProjectilesCodes { get; set; } = "*arrow*";

        // Global capability gates - block a whole class of effect behavior regardless of what any
        // individual item asks for.
        public bool AllowFly { get; set; } = true;
        public bool AllowClimb { get; set; } = true;
        public bool AllowFall { get; set; } = true;
        public bool AllowRefresh { get; set; } = true;
        public bool AllowResize { get; set; } = true;

        public EffectLibConfigSyncPacket ToSyncPacket()
        {
            EffectLibConfigSyncPacket packet = new();
            foreach (PropertyInfo prop in SyncProps)
            {
                switch (prop.GetValue(this))
                {
                    case bool b:
                        packet.Bools[prop.Name] = b;
                        break;
                    case int i:
                        packet.Ints[prop.Name] = i;
                        break;
                    case float f:
                        packet.Floats[prop.Name] = f;
                        break;
                    case string s:
                        packet.Strings[prop.Name] = s;
                        break;
                }
            }
            return packet;
        }

        public void ApplySyncPacket(EffectLibConfigSyncPacket packet)
        {
            foreach (PropertyInfo prop in SyncProps)
            {
                if (prop.PropertyType == typeof(bool) && packet.Bools.TryGetValue(prop.Name, out bool b))
                    prop.SetValue(this, b);
                else if (prop.PropertyType == typeof(int) && packet.Ints.TryGetValue(prop.Name, out int i))
                    prop.SetValue(this, i);
                else if (prop.PropertyType == typeof(float) && packet.Floats.TryGetValue(prop.Name, out float f))
                    prop.SetValue(this, f);
                else if (prop.PropertyType == typeof(string) && packet.Strings.TryGetValue(prop.Name, out string s))
                    prop.SetValue(this, s);
            }

            CoatingPolicy.InvalidateCaches();
        }
    }
}
