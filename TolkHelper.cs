using System;
using System.Runtime.InteropServices;
using MelonLoader;

namespace RimWorldAccess
{
    /// <summary>
    /// Speech priority levels for Tolk screen reader output.
    /// </summary>
    public enum SpeechPriority
    {
        Low,      // Don't interrupt (navigation)
        Normal,   // Interrupt low priority
        High      // Interrupt everything (errors, critical info)
    }

    /// <summary>
    /// Wrapper for the Tolk screen reader library.
    /// Provides direct screen reader integration via native API calls.
    /// </summary>
    public static class TolkHelper
    {
        #region P/Invoke Declarations

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Tolk_Load();

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Tolk_Unload();

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool Tolk_IsLoaded();

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern bool Tolk_Output([MarshalAs(UnmanagedType.LPWStr)] string str, bool interrupt);

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern bool Tolk_Speak([MarshalAs(UnmanagedType.LPWStr)] string str, bool interrupt);

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern IntPtr Tolk_DetectScreenReader();

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool Tolk_HasSpeech();

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool Tolk_HasBraille();

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Tolk_TrySAPI(bool trySAPI);

        // NVDA Controller Client functions for direct testing
        [DllImport("nvdaControllerClient64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvdaController_testIfRunning();

        [DllImport("nvdaControllerClient64.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int nvdaController_speakText([MarshalAs(UnmanagedType.LPWStr)] string text);

        #endregion

        private static bool isInitialized = false;
        private static MelonLogger.Instance logger = null;
        private static bool useDirectNVDA = false;

        /// <summary>
        /// Sets the logger instance for error reporting.
        /// Should be called from rimworld_access.cs during initialization.
        /// </summary>
        public static void SetLogger(MelonLogger.Instance loggerInstance)
        {
            logger = loggerInstance;
        }

        /// <summary>
        /// Initializes the Tolk screen reader library.
        /// Must be called before any other Tolk operations.
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized)
            {
                return;
            }

            try
            {
                // Test NVDA directly first
                bool nvdaRunning = false;
                try
                {
                    int nvdaResult = nvdaController_testIfRunning();
                    nvdaRunning = (nvdaResult == 0);
                    logger?.Msg($"Direct NVDA test: {(nvdaRunning ? "NVDA is running" : $"NVDA not detected (code: {nvdaResult})")}");
                }
                catch (Exception nvdaEx)
                {
                    logger?.Warning($"Could not test NVDA directly: {nvdaEx.Message}");
                }

                // Load Tolk - it will try screen readers first
                Tolk_Load();

                // Enable SAPI fallback AFTER loading (only if no screen reader found)
                Tolk_TrySAPI(true);

                isInitialized = true;

                if (Tolk_IsLoaded())
                {
                    // Get screen reader name
                    IntPtr namePtr = Tolk_DetectScreenReader();
                    string screenReaderName = namePtr != IntPtr.Zero
                        ? Marshal.PtrToStringUni(namePtr)
                        : "Unknown";

                    bool hasSpeech = Tolk_HasSpeech();
                    bool hasBraille = Tolk_HasBraille();

                    logger?.Msg($"Tolk screen reader integration initialized successfully.");
                    logger?.Msg($"Detected screen reader: {screenReaderName}");
                    logger?.Msg($"Speech support: {hasSpeech}");
                    logger?.Msg($"Braille support: {hasBraille}");

                    // If Tolk detected SAPI but we know NVDA is running, use direct NVDA communication
                    if (screenReaderName == "SAPI" && nvdaRunning)
                    {
                        logger?.Warning("Tolk fell back to SAPI even though NVDA is running.");
                        logger?.Msg("Switching to direct NVDA communication mode.");
                        useDirectNVDA = true;
                    }
                }
                else
                {
                    logger?.Warning("Tolk initialized but no screen reader detected.");
                    logger?.Warning("Make sure a screen reader (NVDA, JAWS, etc.) is running before starting RimWorld.");
                }
            }
            catch (DllNotFoundException ex)
            {
                logger?.Error($"Failed to load Tolk.dll: {ex.Message}");
                logger?.Error("Ensure Tolk.dll is in the same directory as rimworld_access.dll");
                throw;
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to initialize Tolk: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Shuts down the Tolk screen reader library.
        /// Should be called during mod cleanup.
        /// </summary>
        public static void Shutdown()
        {
            if (!isInitialized)
            {
                return;
            }

            try
            {
                Tolk_Unload();
                isInitialized = false;
                logger?.Msg("Tolk screen reader integration shut down.");
            }
            catch (Exception ex)
            {
                logger?.Error($"Error shutting down Tolk: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if Tolk is initialized and a screen reader is detected.
        /// </summary>
        public static bool IsActive()
        {
            if (!isInitialized)
            {
                return false;
            }

            try
            {
                return Tolk_IsLoaded();
            }
            catch (Exception ex)
            {
                logger?.Error($"Error checking Tolk status: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sends text to the screen reader for speech output.
        /// </summary>
        /// <param name="text">The text to speak</param>
        /// <param name="priority">Speech priority level (determines interruption behavior)</param>
        public static void Speak(string text, SpeechPriority priority = SpeechPriority.Normal)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (!isInitialized)
            {
                logger?.Warning("Tolk.Speak called but Tolk is not initialized");
                return;
            }

            try
            {
                // If we're in direct NVDA mode, bypass Tolk
                if (useDirectNVDA)
                {
                    try
                    {
                        nvdaController_speakText(text);
                        return;
                    }
                    catch (Exception nvdaEx)
                    {
                        logger?.Warning($"Direct NVDA communication failed: {nvdaEx.Message}, falling back to Tolk");
                        useDirectNVDA = false; // Disable for future calls
                    }
                }

                // Determine interrupt behavior based on priority
                bool interrupt = priority == SpeechPriority.High;

                // Use Tolk_Output which handles both speech and braille
                Tolk_Output(text, interrupt);
            }
            catch (Exception ex)
            {
                logger?.Error($"Error speaking text via Tolk: {ex.Message}");
            }
        }
    }
}
