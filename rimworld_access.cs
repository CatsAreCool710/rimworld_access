using MelonLoader;
using HarmonyLib;

[assembly: MelonInfo(typeof(RimWorldAccess.RimWorldAccessMod), "RimWorld Access", "1.0.0", "Your Name")]
[assembly: MelonGame("Ludeon Studios", "RimWorld by Ludeon Studios")]

namespace RimWorldAccess
{
    public class RimWorldAccessMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            // Initialize the static logger so other classes can use it
            ModLogger.Initialize(LoggerInstance);

            LoggerInstance.Msg("RimWorld Access Mod - Initializing accessibility features...");

            // Initialize Tolk screen reader integration
            try
            {
                TolkHelper.SetLogger(LoggerInstance);
                TolkHelper.Initialize();

                if (TolkHelper.IsActive())
                {
                    TolkHelper.Speak("RimWorld Access mod loaded", SpeechPriority.Normal);
                }
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Error($"Failed to initialize Tolk screen reader integration: {ex.Message}");
                LoggerInstance.Error("The mod will not function without Tolk.dll");
                return;
            }

            // Apply Harmony patches
            var harmony = new HarmonyLib.Harmony("com.rimworldaccess.mainmenukeyboard");

            LoggerInstance.Msg("Applying Harmony patches...");
            harmony.PatchAll();

            // Log which patches were applied
            var patchedMethods = harmony.GetPatchedMethods();
            int patchCount = 0;
            foreach (var method in patchedMethods)
            {
                patchCount++;
                LoggerInstance.Msg($"Patched: {method.DeclaringType?.Name}.{method.Name}");
            }
            LoggerInstance.Msg($"Total patches applied: {patchCount}");

            LoggerInstance.Msg("RimWorld Access Mod - Main menu keyboard navigation enabled!");
            LoggerInstance.Msg("Use Arrow keys to navigate, Enter to select.");
        }

        public override void OnDeinitializeMelon()
        {
            LoggerInstance.Msg("RimWorld Access Mod - Shutting down...");
            TolkHelper.Shutdown();
        }
    }
}
