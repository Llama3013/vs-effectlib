using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace EffectLib
{
    /// <summary>
    /// Effect ids are always "domain:path". A plain id belongs to the mod of the
    /// item declaring it; another mod shares that effect by writing the full id.
    /// </summary>
    public static class EffectIds
    {
        public static string Qualify(string effectId, string domain)
        {
            if (string.IsNullOrWhiteSpace(effectId))
                return null;

            effectId = effectId.ToLowerInvariant();
            return effectId.Contains(':')
                ? effectId
                : $"{(string.IsNullOrWhiteSpace(domain) ? EffectRegistry.DefaultDomain : domain)}:{effectId}";
        }

        // Reads an effect definition's id, qualified with the declaring item's domain.
        public static string Read(JsonObject def, string idField, CollectibleObject owner) =>
            def?.Exists == true ? Qualify(def[idField].AsString(), owner?.Code?.Domain) : null;
    }
}
