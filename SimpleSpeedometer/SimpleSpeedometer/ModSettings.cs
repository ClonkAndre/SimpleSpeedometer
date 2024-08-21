using System.Numerics;

using IVSDKDotNet;
using IVSDKDotNet.Attributes;

namespace SimpleSpeedometer
{
    [ShowStaticFieldsInInspector()]
    internal class ModSettings
    {

        #region Variables
        // General
        [Separator("General")]
        public static bool DisableInMP;

        // Keys
        [Separator("Keys")]
        public static string HazardLightToggleKey;
        public static string HazardLightToggleKeyAlt;
        public static string LeftIndicatorToggleKey;
        public static string LeftIndicatorToggleAltKey;
        public static string RightIndicatorToggleKey;
        public static string RightIndicatorToggleAltKey;

        public static string CruiseControlToggleKey;
        public static string CruiseControlSetTargetSpeedKey;

        public static string SeatbealtToggleKey;

        // Speedometer
        [Separator("Speedometer")]
        public static bool EnableSpeedometer;
        public static bool UseMPH;
        public static string Skin;
        public static bool DoFading;
        public static int FadeInSpeed;
        public static int FadeOutSpeed;

        // Dashboard
        [Separator("Dashboard")]
        public static bool EnableDashboard;
        public static Vector2 DashboardOffset;
        public static bool HideWhenSubtitleIsBeingDisplayed;

        public static bool ShowEngineIcon;
        public static bool ShowABSIcon;
        public static bool ShowParkingBrakeIcon;
        public static bool ShowHeadlightIcon;
        public static bool ShowTemperatureIcon;
        public static bool ShowCruiseControlIcon;
        public static bool ShowTPMSIcon;
        public static bool ShowSeatbeltIcon;

        // Seatbelt
        [Separator("Seatbelt")]
        public static bool EnableSeatbealt;
        public static bool PutSeatbeltOnAutomatically;

        // Fuel
        [Separator("Fuel")]
        public static bool EnableFuel;
        public static bool DisableFuelInMP;
        public static bool DisableWhenOnAMission;
        public static bool UseSimpleFuelConsumptionMethod;
        public static float SimpleFuelConsumptionFixedValue;
        public static float GlobalConsumptionMultiplier;

        public static bool EnableConsumptionForBikes;
        public static float BikeConsumptionMultiplier;

        public static bool EnableConsumptionForCars;
        public static float CarConsumptionMultiplier;
        #endregion

        public static void Init(SettingsFile settings)
        {
            // General
            DisableInMP = settings.GetBoolean("General", "DisableInMP", false);

            // Keys
            HazardLightToggleKey =              settings.GetValue("Keys", "HazardLightToggle", "OemPeriod");
            HazardLightToggleKeyAlt =           settings.GetValue("Keys", "HazardLightToggleAlt", "None");
            LeftIndicatorToggleKey =            settings.GetValue("Keys", "LeftIndicatorToggle", "OemComma");
            LeftIndicatorToggleAltKey =         settings.GetValue("Keys", "LeftIndicatorToggleAlt", "None");
            RightIndicatorToggleKey =           settings.GetValue("Keys", "RightIndicatorToggle", "OemMinus");
            RightIndicatorToggleAltKey =        settings.GetValue("Keys", "RightIndicatorToggleAlt", "None");

            CruiseControlToggleKey =            settings.GetValue("Keys", "CruiseControlToggle", "LShiftKey+B");
            CruiseControlSetTargetSpeedKey =    settings.GetValue("Keys", "CruiseControlSetTargetSpeed", "LShiftKey+N");

            SeatbealtToggleKey =                settings.GetValue("Keys", "SeatbealtToggle", "LShiftKey+M");

            // Speedometer
            EnableSpeedometer = settings.GetBoolean("Speedometer", "Enable", true);
            UseMPH =            settings.GetBoolean("Speedometer", "UseMPH", true);
            Skin =              settings.GetValue("Speedometer", "Skin", "Default_MPH");
            DoFading =          settings.GetBoolean("Speedometer", "DoFading", false);
            FadeInSpeed =       settings.GetInteger("Speedometer", "FadeInSpeed", 4);
            FadeOutSpeed =      settings.GetInteger("Speedometer", "FadeOutSpeed", 6);

            // Dashboard
            EnableDashboard =                   settings.GetBoolean("Dashboard", "Enable", true);
            DashboardOffset =                   settings.GetVector2("Dashboard", "Offset", new Vector2(100f, 90f));
            HideWhenSubtitleIsBeingDisplayed =  settings.GetBoolean("Dashboard", "HideWhenSubtitleIsBeingDisplayed", true);

            ShowEngineIcon =        settings.GetBoolean("Dashboard", "ShowEngineIcon", true);
            ShowABSIcon =           settings.GetBoolean("Dashboard", "ShowABSIcon", true);
            ShowParkingBrakeIcon =  settings.GetBoolean("Dashboard", "ShowParkingBrakeIcon", true);
            ShowHeadlightIcon =     settings.GetBoolean("Dashboard", "ShowHeadlightIcon", true);
            ShowTemperatureIcon =   settings.GetBoolean("Dashboard", "ShowTemperatureIcon", true);
            ShowCruiseControlIcon = settings.GetBoolean("Dashboard", "ShowCruiseControlIcon", true);
            ShowTPMSIcon =          settings.GetBoolean("Dashboard", "ShowTPMSIcon", true);
            ShowSeatbeltIcon =      settings.GetBoolean("Dashboard", "ShowSeatbeltIcon", true);

            // Seatbelt
            EnableSeatbealt =               settings.GetBoolean("Seatbelt", "Enable", false);
            PutSeatbeltOnAutomatically =    settings.GetBoolean("Seatbelt", "PutOnAutomatically", false);

            // Fuel
            EnableFuel =                        settings.GetBoolean("Fuel", "Enable", true);
            DisableFuelInMP =                   settings.GetBoolean("Fuel", "DisableInMP", true);
            DisableWhenOnAMission =             settings.GetBoolean("Fuel", "DisableWhenOnAMission", true);
            UseSimpleFuelConsumptionMethod =    settings.GetBoolean("Fuel", "UseSimpleFuelConsumptionMethod", false);
            SimpleFuelConsumptionFixedValue =   settings.GetFloat("Fuel", "SimpleFuelConsumptionFixedValue", 0.05f);
            GlobalConsumptionMultiplier =       settings.GetFloat("Fuel", "GlobalConsumptionMultiplier", 1.0f);

            EnableConsumptionForBikes =         settings.GetBoolean("Fuel", "EnableConsumptionForBikes", true);
            BikeConsumptionMultiplier =         settings.GetFloat("Fuel", "BikeConsumptionMultiplier", 0.5f);

            EnableConsumptionForCars =          settings.GetBoolean("Fuel", "EnableConsumptionForCars", true);
            CarConsumptionMultiplier =          settings.GetFloat("Fuel", "CarConsumptionMultiplier", 1.0f);
        }

        public static void Save(SettingsFile settings)
        {
            // General
            settings.SetBoolean("General", "DisableInMP", DisableInMP);

            // Keys
            settings.SetValue("Keys", "HazardLightToggle", HazardLightToggleKey);
            settings.SetValue("Keys", "HazardLightToggleAlt", HazardLightToggleKeyAlt);
            settings.SetValue("Keys", "LeftIndicatorToggle", LeftIndicatorToggleKey);
            settings.SetValue("Keys", "LeftIndicatorToggleAlt", LeftIndicatorToggleAltKey);
            settings.SetValue("Keys", "RightIndicatorToggle", RightIndicatorToggleKey);
            settings.SetValue("Keys", "RightIndicatorToggleAlt", RightIndicatorToggleAltKey);

            settings.SetValue("Keys", "CruiseControlToggle", CruiseControlToggleKey);
            settings.SetValue("Keys", "CruiseControlSetTargetSpeed", CruiseControlSetTargetSpeedKey);

            settings.SetValue("Keys", "SeatbealtToggle", SeatbealtToggleKey);

            // Speedometer
            settings.SetBoolean("Speedometer", "Enable", EnableSpeedometer);
            settings.SetBoolean("Speedometer", "UseMPH", UseMPH);
            settings.SetValue("Speedometer", "Skin", Skin);
            settings.SetBoolean("Speedometer", "DoFading", DoFading);
            settings.SetInteger("Speedometer", "FadeInSpeed", FadeInSpeed);
            settings.SetInteger("Speedometer", "FadeOutSpeed", FadeOutSpeed);

            // Dashboard
            settings.SetBoolean("Dashboard", "Enable", EnableDashboard);
            settings.SetVector2("Dashboard", "Offset", DashboardOffset);
            settings.SetBoolean("Dashboard", "HideWhenSubtitleIsBeingDisplayed", HideWhenSubtitleIsBeingDisplayed);

            settings.SetBoolean("Dashboard", "ShowEngineIcon", ShowEngineIcon);
            settings.SetBoolean("Dashboard", "ShowABSIcon", ShowABSIcon);
            settings.SetBoolean("Dashboard", "ShowParkingBrakeIcon", ShowParkingBrakeIcon);
            settings.SetBoolean("Dashboard", "ShowHeadlightIcon", ShowHeadlightIcon);
            settings.SetBoolean("Dashboard", "ShowTemperatureIcon", ShowTemperatureIcon);
            settings.SetBoolean("Dashboard", "ShowCruiseControlIcon", ShowCruiseControlIcon);
            settings.SetBoolean("Dashboard", "ShowTPMSIcon", ShowTPMSIcon);
            settings.SetBoolean("Dashboard", "ShowSeatbeltIcon", ShowSeatbeltIcon);

            // Seatbelt
            settings.SetBoolean("Seatbelt", "Enable", EnableSeatbealt);
            settings.SetBoolean("Seatbelt", "PutOnAutomatically", PutSeatbeltOnAutomatically);

            // Fuel
            settings.SetBoolean("Fuel", "Enable", EnableFuel);
            settings.GetBoolean("Fuel", "DisableInMP", DisableFuelInMP);
            settings.SetBoolean("Fuel", "DisableWhenOnAMission", DisableWhenOnAMission);
            settings.SetBoolean("Fuel", "UseSimpleFuelConsumptionMethod", UseSimpleFuelConsumptionMethod);
            settings.SetFloat("Fuel", "SimpleFuelConsumptionFixedValue", SimpleFuelConsumptionFixedValue);
            settings.SetFloat("Fuel", "GlobalConsumptionMultiplier", GlobalConsumptionMultiplier);
            settings.SetFloat("Fuel", "BikeConsumptionMultiplier", BikeConsumptionMultiplier);
            settings.SetFloat("Fuel", "CarConsumptionMultiplier", CarConsumptionMultiplier);

            settings.Save();
        }

    }
}
