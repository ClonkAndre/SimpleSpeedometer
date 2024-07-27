using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Numerics;

using CCL.GTAIV;
using CCL.GTAIV.Extensions;

using IVSDKDotNet;
using IVSDKDotNet.Attributes;
using IVSDKDotNet.Enums;
using static IVSDKDotNet.Native.Natives;

namespace SimpleSpeedometer
{
    public class Main : Script
    {

        #region Variables
        private bool allowDrawing;

        private bool playerInCar;
        private IVPed playerPed;
        private IVVehicle currentVehicle;
        private IVVehicle previousVehicle;
        private float currentVehicleSpeedRaw;
        private float currentVehicleSpeed;

        private bool isUsingController;

        // Models
        private uint ronGasPumpModel;
        private uint globeOilGasPumpModel;
        private uint terrOilGasPumpModel;

        // Textures
        private IntPtr digitsTexture;
        private IntPtr needleTexture;
        private IntPtr leftIndicatorTexture;
        private IntPtr rightIndicatorTexture;
        private IntPtr absTexture;
        private IntPtr lightsTexture;
        private IntPtr engineTexture;
        private IntPtr brakeTexture;
        private IntPtr temperatureTexture;
        private IntPtr cruiseControlTexture;
        private IntPtr tpmsTexture;
        private IntPtr seatbeltTexture;

        // Speedometer
        private int textureAlphaValue;
        private float digitsAndNeedleOffsetX, digitsAndNeedleOffsetY;
        private float digitsAndNeedleSizeWidth, digitsAndNeedleSizeHeight;

        // Fuel
        private int RonPricePerLiter = 1;
        private int GlobeOilPricePerLiter = 1;
        private int TerrOilPricePerLiter = 1;
        private bool isFillingUpCar;
        private bool suppressNotificationSound;
        private bool showedLowFuelWarningMessage;
        private GasStation previousGasStation;
        private GasStation currentGasStation;

        // Seatbelt
        private bool wasSeatbeltReset;

        // Blinking
        private static readonly object fastBlinkingLockObj = new object();
        private bool fastBlinking;
        private Guid fastBlinkingTimer;

        private static readonly object mediumBlinkingLockObj = new object();
        private bool mediumBlinking;
        private Guid mediumBlinkingTimer;

        // Indicator stuff
        private Stopwatch leftIndicatorKeyWatch;
        private Stopwatch rightIndicatorKeyWatch;
        private bool hazardLightsOn;
        private bool indicatorLeftOn;
        private bool indicatorRightOn;

        // Cruise Control
        [Separator("Cruise Control")]
#if DEBUG
        public bool EnableCruiseControlDebug;
#endif
        public bool CruiseControlEnabled;
        public float CruiseControlTargetSpeed;
        private float cruiseControlTargetSpeedMultiplier;

        public float CruiseControlGasPedalSensitivity = 0.1f;
        public float CruiseControlBrakePedalSensitivity = 0.3f;

        public bool UseAdaptiveCruiseControl = true;
        public int CruiseControlSafetyDistance = 3;

        // Fading
        private bool isFadedOut;
        private bool isFadedIn;
        #endregion

        #region Constructor
        public Main()
        {
            leftIndicatorKeyWatch = new Stopwatch();
            rightIndicatorKeyWatch = new Stopwatch();
            
            // IV-SDK .NET stuff
            Initialized += Main_Initialized;
            Uninitialize += Main_Uninitialize;
            OnFirstD3D9Frame += Main_OnFirstD3D9Frame;
            OnImGuiRendering += Main_OnImGuiRendering;
            ProcessPad += Main_ProcessPad;
            ProcessAutomobile += Main_ProcessAutomobile;
            Tick += Main_Tick;
            ScriptCommandReceived += Main_ScriptCommandReceived;
        }
        #endregion

        #region Methods
        private void Reset()
        {
            currentVehicle = null;
            previousVehicle = null;
            playerInCar = false;

            // Speedometer
            allowDrawing = false;
            textureAlphaValue = 0;
            currentVehicleSpeedRaw = 0f;
            currentVehicleSpeed = 0f;

            // Fuel
            showedLowFuelWarningMessage = false;

            // Seatbelt
            if (ModSettings.EnableSeatbealt)
            {
                if (!wasSeatbeltReset)
                {
                    playerPed.PedFlags2.WillFlyThroughWindscreen = true;
                    wasSeatbeltReset = true;
                }
            }

            // Indicator stuff
            hazardLightsOn = false;
            indicatorLeftOn = false;
            indicatorRightOn = false;

            // Cruise Control
            CruiseControlEnabled = false;
            CruiseControlTargetSpeed = 0f;
            cruiseControlTargetSpeedMultiplier = 1.0f;
        }
        private void CalculateTextureFadingValues()
        {
            if (!playerInCar || IS_PAUSE_MENU_ACTIVE() || IS_SCREEN_FADING_OUT() || IS_SCREEN_FADED_OUT())
            {
                // Fade out
                if (ModSettings.DoFading)
                {

                    // Handle fade out
                    int previousAlphaValue = textureAlphaValue;

                    previousAlphaValue -= ModSettings.FadeOutSpeed;

                    if (previousAlphaValue < 0)
                        previousAlphaValue = 0;

                    textureAlphaValue = previousAlphaValue;

                }
                else
                {
                    textureAlphaValue = 0;
                }
            }
            else
            {
                // Fade in but only when player is in a car
                if (playerInCar)
                {
                    if (ModSettings.DoFading)
                    {

                        // Handle fade in
                        int previousAlphaValue = textureAlphaValue;

                        previousAlphaValue += ModSettings.FadeInSpeed;

                        if (previousAlphaValue > 255)
                            previousAlphaValue = 255;

                        textureAlphaValue = previousAlphaValue;

                    }
                    else
                    {
                        textureAlphaValue = 255;
                    }
                }
            }

            // Set states
            isFadedOut = textureAlphaValue <= 0;
            isFadedIn = textureAlphaValue >= 255;
        }

        private void CheckFuelStuff()
        {
            if (!ModSettings.EnableFuel)
                return;

            if (IVNetwork.IsNetworkGameRunning())
            {
                if (ModSettings.DisableFuelInMP)
                    return;
            }
            else
            {
                if (ModSettings.DisableWhenOnAMission && GET_MISSION_FLAG())
                    return;
            }

            // If the current player ped is not the driver of the car then only check the engine fuel
            if (!currentVehicle.IsDriver(playerPed))
            {

                if (currentVehicle.VehicleFlags.EngineOn && currentVehicle.PetrolTankHealth <= 0f)
                    currentVehicle.VehicleFlags4.OldCarExhaustFX = true;

                return;
            }

            // Get the id of the local player
            int playerID = (int)GET_PLAYER_ID();

            // Consume fuel if engine is on
            if (currentVehicle.VehicleFlags.EngineOn)
            {
                // Calculate fuel consumption value
                float fuelConsumptionSpeed = 0f;

                if (!ModSettings.UseSimpleFuelConsumptionMethod)
                {
                    fuelConsumptionSpeed = (currentVehicle.Handling.DragMult * 2500f) * currentVehicle.EngineRPM / 100f;
                    fuelConsumptionSpeed = fuelConsumptionSpeed * ((1000f - currentVehicle.Handling.Mass) / 1000f) + fuelConsumptionSpeed;
                }
                else
                {
                    fuelConsumptionSpeed = currentVehicle.EngineRPM * ModSettings.SimpleFuelConsumptionFixedValue;
                }

                if (fuelConsumptionSpeed < 0f)
                    fuelConsumptionSpeed = fuelConsumptionSpeed * -1f;

                // Get model type of vehicle
                GET_CAR_MODEL(currentVehicle.GetHandle(), out uint modelValue);

                if (IS_THIS_MODEL_A_BIKE(modelValue))
                {
                    if (!ModSettings.EnableConsumptionForBikes)
                        return;

                    fuelConsumptionSpeed = fuelConsumptionSpeed * ModSettings.BikeConsumptionMultiplier;
                }
                else if (IS_THIS_MODEL_A_CAR(modelValue))
                {
                    if (!ModSettings.EnableConsumptionForCars)
                        return;

                    fuelConsumptionSpeed = fuelConsumptionSpeed * ModSettings.CarConsumptionMultiplier;
                }
                else
                {
                    return;
                }

                // Consume fuel
                float petrolTankHealth = currentVehicle.PetrolTankHealth;

                petrolTankHealth -= fuelConsumptionSpeed;

                if (petrolTankHealth < 0f)
                {
                    petrolTankHealth = 0f;

                    // Disable engine
                    ShowSubtitleMessage("Your vehicle ran out of fuel.");
                    currentVehicle.VehicleFlags4.OldCarExhaustFX = true;
                    currentVehicle.VehicleFlags.EngineOn = false;
                }
                else if (petrolTankHealth < 100f)
                {
                    if (!showedLowFuelWarningMessage && !ModSettings.EnableDashboard)
                    {
                        ShowSubtitleMessage("Your vehicle is running low on fuel!", 3000);
                        showedLowFuelWarningMessage = true;
                    }
                }
                else
                {
                    showedLowFuelWarningMessage = false;
                }

                currentVehicle.PetrolTankHealth = petrolTankHealth;
            }

            // Check if fill up key is pressed
            IVPad pad = IVPad.GetPad();
            
            if (isUsingController)
                isFillingUpCar = pad.Values[(int)ePadControls.INPUT_FRONTEND_ACCEPT].CurrentValue == 255;
            else
                isFillingUpCar = pad.Values[(int)ePadControls.INPUT_PICKUP].CurrentValue == 255;

            // Check if player is near a fuel pump
            IVPool objectPool = IVPools.GetObjectPool();
            for (int i = 0; i < objectPool.Count; i++)
            {
                UIntPtr objPtr = objectPool.Get(i);

                // Check if obj is valid
                if (objPtr == UIntPtr.Zero)
                    continue;

                // First of all, check if player is kinda standing still
                if (currentVehicleSpeedRaw > 3f)
                    continue;

                // Get the object handle
                int objHandle = (int)objectPool.GetIndex(objPtr);

                // Get the object model and check if it's a fuel pump
                GET_OBJECT_MODEL(objHandle, out uint objModelValue);

                // Figure out the gas station the player might be standing at
                bool ronStation =       objModelValue == ronGasPumpModel;
                bool globeOilStation =  objModelValue == globeOilGasPumpModel;
                bool terrOilStation =   objModelValue == terrOilGasPumpModel;

                if (!ronStation && !globeOilStation && !terrOilStation)
                    continue;

                // Get the gas station details
                string gasStationHeadline = "Welcome!";
                int pricePerLiter = 1;

                if (ronStation)
                {
                    gasStationHeadline = "Welcome to RON!";
                    pricePerLiter = RonPricePerLiter;
                    currentGasStation = GasStation.Ron;
                }
                if (globeOilStation)
                {
                    gasStationHeadline = "Welcome to Globe Oil!";
                    pricePerLiter = GlobeOilPricePerLiter;
                    currentGasStation = GasStation.GlobeOil;
                }
                if (terrOilStation)
                {
                    gasStationHeadline = "Welcome to Terroil!";
                    pricePerLiter = TerrOilPricePerLiter;
                    currentGasStation = GasStation.Terroil;
                }

                // Get the offset position of the object
                GET_OFFSET_FROM_OBJECT_IN_WORLD_COORDS(objHandle, new Vector3(0f, 2f, 0f),  out Vector3 posFront);
                GET_OFFSET_FROM_OBJECT_IN_WORLD_COORDS(objHandle, new Vector3(0f, -2f, 0f), out Vector3 posBack);

                // Check distance
                bool closeToFrontOfObject = Vector3.Distance(playerPed.Matrix.Pos, posFront) < 5f;
                bool closeToBackOfObject =  Vector3.Distance(playerPed.Matrix.Pos, posBack) < 5f;

                if (!(closeToFrontOfObject && closeToBackOfObject))
                {
                    // Hide help message
                    if (IS_THIS_HELP_MESSAGE_BEING_DISPLAYED("TM_1_2"))
                        CLEAR_HELP();

                    // Reset current gas station
                    currentGasStation = GasStation.None;

                    continue;
                }

                // Handle car fill up logic and notification logic
                if (isFillingUpCar)
                {
                    if (IS_THIS_HELP_MESSAGE_BEING_DISPLAYED("TM_1_2"))
                        CLEAR_HELP();

                    // Check if player has enough money to buy some fuel
                    STORE_SCORE(playerID, out uint money);
                    if (money < pricePerLiter && !IVNetwork.IsNetworkGameRunning())
                    {
                        NativeGame.DisplayCustomHelpMessage("You currently don't have enough funds to fill up your car.", true);
                        break;
                    }

                    // Fill up
                    float health = currentVehicle.PetrolTankHealth;
                    health += 1f;

                    if (health > 1000f)
                        health = 1000f;
                    else
                    {
                        if (!IVNetwork.IsNetworkGameRunning()) // Remove money from player if not in multiplayer mode
                            ADD_SCORE(playerID, -1 * pricePerLiter);
                    }

                    currentVehicle.PetrolTankHealth = health;

                    // Calculate fill up percentage
                    int fillUpPercantage = (int)(health / 10f);

                    switch (currentGasStation)
                    {
                        case GasStation.Ron:
                            NativeGame.DisplayCustomHelpMessage(string.Format("Your car is {0}% filled up with RON fuel. Winner fuel that is.", fillUpPercantage), suppressNotificationSound, false, "TM_1_4");
                            break;
                        case GasStation.GlobeOil:
                            NativeGame.DisplayCustomHelpMessage(string.Format("Your car is {0}% filled up with premium fuel from Globe Oil.", fillUpPercantage), suppressNotificationSound, false, "TM_1_4");
                            break;
                        case GasStation.Terroil:
                            NativeGame.DisplayCustomHelpMessage(string.Format("Your car is {0}% filled up with the best foreign oil you can get from Terroil.", fillUpPercantage), suppressNotificationSound, false, "TM_1_4");
                            break;
                    }
                }
                else
                {
                    // Reset suppress notifcation sound variable if player is at a different station
                    if (previousGasStation != currentGasStation)
                    {
                        previousGasStation = currentGasStation;
                        suppressNotificationSound = false;
                    }

                    // Calculate what a full fill up would cost
                    int fullFillUpCost = (int)-((currentVehicle.PetrolTankHealth - 1000f) * pricePerLiter);

                    // Show notification to player
                    if (fullFillUpCost > 4)
                    {
                        if (IS_THIS_HELP_MESSAGE_BEING_DISPLAYED("TM_1_3") || IS_THIS_HELP_MESSAGE_BEING_DISPLAYED("TM_1_4"))
                            CLEAR_HELP();

                        if (!IVNetwork.IsNetworkGameRunning())
                        {
                            NativeGame.DisplayCustomHelpMessage(string.Format("{0}~n~Hold {1} to fill up your car. The current price for one liter is ${2}.~n~A full fill up would cost you ${3}", gasStationHeadline, isUsingController ? "~INPUT_FRONTEND_ACCEPT~" : "~INPUT_PICKUP~", pricePerLiter, fullFillUpCost), suppressNotificationSound, false, "TM_1_2");
                        }
                        else
                        {
                            switch (currentGasStation)
                            {
                                case GasStation.Ron:
                                    NativeGame.DisplayCustomHelpMessage(string.Format("{0}~n~Hold {1} to fill up your car. This one will be on the house. But only because we believe you are a winner, and only winners, put RON in their tank.", gasStationHeadline, isUsingController ? "~INPUT_FRONTEND_ACCEPT~" : "~INPUT_PICKUP~"), suppressNotificationSound, false, "TM_1_2");
                                    break;
                                case GasStation.GlobeOil:
                                    NativeGame.DisplayCustomHelpMessage(string.Format("{0}~n~Hold {1} to fill up your car. As a thanks for changing the climate, this one will be on the house.", gasStationHeadline, isUsingController ? "~INPUT_FRONTEND_ACCEPT~" : "~INPUT_PICKUP~"), suppressNotificationSound, false, "TM_1_2");
                                    break;
                                case GasStation.Terroil:
                                    NativeGame.DisplayCustomHelpMessage(string.Format("{0}~n~Hold {1} to fill up your car. This one will be on the house. Thanks for supporting america.", gasStationHeadline, isUsingController ? "~INPUT_FRONTEND_ACCEPT~" : "~INPUT_PICKUP~"), suppressNotificationSound, false, "TM_1_2");
                                    break;
                            }
                        }
                    }
                    else
                    {
                        if (IS_THIS_HELP_MESSAGE_BEING_DISPLAYED("TM_1_2") || IS_THIS_HELP_MESSAGE_BEING_DISPLAYED("TM_1_4"))
                            CLEAR_HELP();

                        switch (currentGasStation)
                        {
                            case GasStation.Ron:
                                NativeGame.DisplayCustomHelpMessage(string.Format("{0}~n~Winners put RON in their tanks. If you are a winner, and need some fuel, then put RON in your tank!", gasStationHeadline), suppressNotificationSound, false, "TM_1_3");
                                break;
                            case GasStation.GlobeOil:
                                NativeGame.DisplayCustomHelpMessage(string.Format("{0}~n~The best, and most premium fuel you can get. Don't you ever worry about the things you read about us online. We at Globe Oil, change the Climate. Powering The Future.", gasStationHeadline), suppressNotificationSound, false, "TM_1_3");
                                break;
                            case GasStation.Terroil:
                                NativeGame.DisplayCustomHelpMessage(string.Format("{0}~n~Get the original & best fuel you could ever think of! Support America - Use Foreign Oil.", gasStationHeadline), suppressNotificationSound, false, "TM_1_3");
                                break;
                        }
                    }

                    // Set suppress notifcation sound variable to true so "bing" sound only appears once for the current station the player is at
                    if (!suppressNotificationSound)
                        suppressNotificationSound = true;
                }

            }
        }
        private void CheckCruiseControlStuff()
        {
            if (!IsCurrentVehicleValid())
                return;
            if (!CruiseControlEnabled)
                return;
            if (CruiseControlTargetSpeed.InBetween(-1f, 1f))
                return;

            if (UseAdaptiveCruiseControl)
            {
                int handle = currentVehicle.GetHandle();

                float steer = 0f;
                if (isUsingController)
                {
                    steer = ImGuiIV.GetKeyAnalogValue(eImGuiKey.ImGuiKey_GamepadLStickRight) - ImGuiIV.GetKeyAnalogValue(eImGuiKey.ImGuiKey_GamepadLStickLeft);
                }
                else
                {
                    float aKeyValue = ImGuiIV.IsKeyDown(eImGuiKey.ImGuiKey_A) || ImGuiIV.IsKeyDown(eImGuiKey.ImGuiKey_LeftArrow) ? -0.5f : 0.0f;
                    float dKeyValue = ImGuiIV.IsKeyDown(eImGuiKey.ImGuiKey_D) || ImGuiIV.IsKeyDown(eImGuiKey.ImGuiKey_RightArrow) ? -0.5f : 0.0f;
                    steer = aKeyValue - dKeyValue;
                }

                // Get dimensions of the current vehicle
                GET_CAR_MODEL(handle, out uint v);
                GET_MODEL_DIMENSIONS(v, out Vector3 min, out Vector3 max);

                // Get offset position for source check
                GET_OFFSET_FROM_CAR_IN_WORLD_COORDS(handle, new Vector3(0f, max.Y, 0.25f), out Vector3 sourceMiddle);

                // Get offset position for target check
                GET_OFFSET_FROM_CAR_IN_WORLD_COORDS(handle, new Vector3(-1.1f, max.Y + CruiseControlTargetSpeed, 0f) + new Vector3(3.5f, 0f, 0f) * steer, out Vector3 targetLeft);
                GET_OFFSET_FROM_CAR_IN_WORLD_COORDS(handle, new Vector3(0f, max.Y + CruiseControlTargetSpeed, 0f) + new Vector3(3.5f, 0f, 0f) * steer, out Vector3 targetMiddle);
                GET_OFFSET_FROM_CAR_IN_WORLD_COORDS(handle, new Vector3(1.1f, max.Y + CruiseControlTargetSpeed, 0f) + new Vector3(3.5f, 0f, 0f) * steer, out Vector3 targetRight);

                // Place the target offset position always above ground for better detections
                targetLeft =    new Vector3(targetLeft.X, targetLeft.Y,     NativeWorld.GetGroundZ(targetLeft, GroundType.Closest) + 0.3f);
                targetMiddle =  new Vector3(targetMiddle.X, targetMiddle.Y, NativeWorld.GetGroundZ(targetMiddle, GroundType.Closest) + 0.3f);
                targetRight =   new Vector3(targetRight.X, targetRight.Y,   NativeWorld.GetGroundZ(targetRight, GroundType.Closest) + 0.3f);

#if DEBUG
                // Debug
                if (EnableCruiseControlDebug)
                {
                    // Source
                    NativeCheckpoint.DrawCheckpoint(sourceMiddle, 0.5f, Color.Yellow);

                    // Target
                    NativeCheckpoint.DrawCheckpoint(targetLeft, 0.5f, Color.Red);
                    NativeCheckpoint.DrawCheckpoint(targetMiddle, 0.5f, Color.Yellow);
                    NativeCheckpoint.DrawCheckpoint(targetRight, 0.5f, Color.Green);
                }
#endif

                // Check if there is any obstacle in the way
                eLineOfSightFlags flags =   eLineOfSightFlags.VEHICLES | eLineOfSightFlags.OBJECTS | eLineOfSightFlags.PEDS_BOUNDING_BOX | eLineOfSightFlags.STATIC_COLLISION;
                bool checkTargetLeft =      IVWorld.ProcessLineOfSight(sourceMiddle, targetLeft, UIntPtr.Zero, out IVLineOfSightResults resLeft, (uint)flags, 1, 0, 2, 4);
                bool checkTargetMiddle =    IVWorld.ProcessLineOfSight(sourceMiddle, targetMiddle, UIntPtr.Zero, out IVLineOfSightResults resMiddle, (uint)flags, 1, 0, 2, 4);
                bool checkTargetRight =     IVWorld.ProcessLineOfSight(sourceMiddle, targetRight, UIntPtr.Zero, out IVLineOfSightResults resRight, (uint)flags, 1, 0, 2, 4);

                if (!checkTargetLeft && !checkTargetMiddle && !checkTargetRight)
                {
                    // If there is no obstacle infront of the vehicle then "slowly" increase cruiseControlTargetSpeedMultiplier value so car can accelerate smoothly
                    float value = cruiseControlTargetSpeedMultiplier + 0.01f;

                    if (value < 0.0f)
                        value = 0.0f;
                    if (value > 1.0f)
                        value = 1.0f;

                    cruiseControlTargetSpeedMultiplier = value;
                }
                else
                {
                    float distanceRaw = 0f;

                    // Get info from hit point
                    if (checkTargetLeft)
                    {
                        distanceRaw = Vector3.Distance(sourceMiddle, resLeft.EndPosition);

#if DEBUG
                        // Debug show hit pos
                        if (EnableCruiseControlDebug)
                            NativeCheckpoint.DrawCheckpoint(resLeft.EndPosition, 0.5f, Color.White);
#endif
                    }
                    if (checkTargetMiddle)
                    {
                        distanceRaw = Vector3.Distance(sourceMiddle, resMiddle.EndPosition);

#if DEBUG
                        // Debug show hit pos
                        if (EnableCruiseControlDebug)
                            NativeCheckpoint.DrawCheckpoint(resMiddle.EndPosition, 0.5f, Color.White);
#endif
                    }
                    if (checkTargetRight)
                    {
                        distanceRaw = Vector3.Distance(sourceMiddle, resRight.EndPosition);

#if DEBUG
                        // Debug show hit pos
                        if (EnableCruiseControlDebug)
                            NativeCheckpoint.DrawCheckpoint(resRight.EndPosition, 0.5f, Color.White);
#endif
                    }

                    float interpolationValue = (distanceRaw + -CruiseControlSafetyDistance) / 10f;
                    cruiseControlTargetSpeedMultiplier = Lerp(0.0f, 1.0f, interpolationValue);
                }
            }
            else
            {
                cruiseControlTargetSpeedMultiplier = 1.0f;
            }
        }
        #endregion

        #region Functions
        private bool IsCurrentVehicleValid()
        {
            return playerInCar && currentVehicle != null;
        }
        private bool IsVehicleTypeValidForDashboard()
        {
            eVehicleType type = (eVehicleType)currentVehicle.VehicleType;

            return type == eVehicleType.VEHICLE_TYPE_AUTOMOBILE
                || type == eVehicleType.VEHICLE_TYPE_BIKE;
        }

        private float Lerp(float a, float b, float t)
        {
            // Clamp t between 0 and 1
            t = Math.Max(0.0f, Math.Min(1.0f, t));

            return a + (b - a) * t;
        }
        #endregion

        private void Main_Initialized(object sender, EventArgs e)
        {
            // Load mod settings
            ModSettings.Init(Settings);

            // Get some models
            ronGasPumpModel =       RAGE.AtStringHash("bm_gaspump"); // RON
            globeOilGasPumpModel =  RAGE.AtStringHash("bm_gaspump2"); // GLOBE OIL
            terrOilGasPumpModel =   RAGE.AtStringHash("bm_gaspump3"); // TERROIL

            // Start timers
            fastBlinkingTimer = StartNewTimer(200, () =>
            {
                lock (fastBlinkingLockObj)
                {
                    fastBlinking = !fastBlinking;
                }
            });
            mediumBlinkingTimer = StartNewTimer(500, () =>
            {
                lock (mediumBlinkingLockObj)
                {
                    mediumBlinking = !mediumBlinking;
                }
            });
        }
        private void Main_Uninitialize(object sender, EventArgs e)
        {
            // Stop timers
            AbortTaskOrTimer(fastBlinkingTimer);
            AbortTaskOrTimer(mediumBlinkingTimer);
        }

        private void Main_OnFirstD3D9Frame(IntPtr devicePtr)
        {
            string digitsFilePath = string.Format("{0}\\{1}\\digits.dds", ScriptResourceFolder, ModSettings.Skin);
            string needleFilePath = string.Format("{0}\\{1}\\needle.dds", ScriptResourceFolder, ModSettings.Skin);

            // Check if both files exists
            if (!File.Exists(digitsFilePath) || !File.Exists(needleFilePath))
            {
                IVGame.Console.PrintWarning(string.Format("[SimpleSpeedometer] Please make sure that 'digits.dds' and 'pin.dds' exists in the 'SimpleSpeedometer\\{0}' folder. Script will be aborted.", ModSettings.Skin));
                Abort();
                return;
            }

            string configFilePath = string.Format("{0}\\{1}\\config.ini", ScriptResourceFolder, ModSettings.Skin);

            // Check if config.ini exists
            if (!File.Exists(configFilePath))
            {
                IVGame.Console.PrintWarning(string.Format("[SimpleSpeedometer] Please make sure that 'config.ini' exists in the 'SimpleSpeedometer\\{0}' folder. Script will be aborted.", ModSettings.Skin));
                Abort();
                return;
            }

            // Load config file
            SettingsFile config = new SettingsFile(configFilePath);

            if (!config.Load())
            {
                IVGame.Console.PrintWarning("[SimpleSpeedometer] Failed to load config file! Script will be aborted.");
                Abort();
                return;
            }
            else
            {
                // Size
                digitsAndNeedleSizeWidth = config.GetFloat("Size", "DigitsAndNeedleSizeWidth", 128);
                digitsAndNeedleSizeHeight = config.GetFloat("Size", "DigitsAndNeedleSizeHeight", 128);

                // Offset
                digitsAndNeedleOffsetX = config.GetFloat("Offset", "DigitsAndNeedleOffsetX", 64f);
                digitsAndNeedleOffsetY = config.GetFloat("Offset", "DigitsAndNeedleOffsetY", 64f);
            }

            // Create digits texture
            if (ImGuiIV.CreateTextureFromFile(digitsFilePath, out IntPtr ptr, out int width, out int height, out eResult result))
                digitsTexture = ptr;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create 'digits' texture!");

            // Create needle texture
            if (ImGuiIV.CreateTextureFromFile(needleFilePath, out ptr, out width, out height, out result))
                needleTexture = ptr;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create 'pin' texture!");

            // Create left indicator texture
            if (ImGuiIV.CreateTextureFromMemory(Properties.Resources.leftIndicatorIcon, out ptr, out width, out height, out result))
                leftIndicatorTexture = ptr;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create texture for right indicator!");

            // Create right indicator texture
            if (ImGuiIV.CreateTextureFromMemory(Properties.Resources.rightIndicatorIcon, out ptr, out width, out height, out result))
                rightIndicatorTexture = ptr;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create texture for left indicator!");

            // Create abs texture
            if (ImGuiIV.CreateTextureFromMemory(Properties.Resources.absIcon, out ptr, out width, out height, out result))
                absTexture = ptr;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create texture for abs!");

            // Create lights texture
            if (ImGuiIV.CreateTextureFromMemory(Properties.Resources.lightsIcon, out ptr, out width, out height, out result))
                lightsTexture = ptr;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create texture for lights!");

            // Create engine texture
            if (ImGuiIV.CreateTextureFromMemory(Properties.Resources.engineIcon, out ptr, out width, out height, out result))
                engineTexture = ptr;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create texture for engine!");

            // Create brake texture
            if (ImGuiIV.CreateTextureFromMemory(Properties.Resources.parkingBreakIcon, out ptr, out width, out height, out result))
                brakeTexture = ptr;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create texture for brake!");

            // Create temperature texture
            if (ImGuiIV.CreateTextureFromMemory(Properties.Resources.temperatureIcon, out ptr, out width, out height, out result))
                temperatureTexture = ptr;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create texture for temperature!");

            // Create cruise control texture
            if (ImGuiIV.CreateTextureFromMemory(Properties.Resources.cruiseControlIcon, out ptr, out width, out height, out result))
                cruiseControlTexture = ptr;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create texture for cruise control!");

            // Create tpms texture
            if (ImGuiIV.CreateTextureFromMemory(Properties.Resources.tpmsIcon, out ptr, out width, out height, out result))
                tpmsTexture = ptr;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create texture for TPSM!");

            // Create seatbelt texture
            if (ImGuiIV.CreateTextureFromMemory(Properties.Resources.seatbeltIcon, out ptr, out width, out height, out result))
                seatbeltTexture = ptr;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create texture for seatbelt!");
        }
        private void Main_OnImGuiRendering(IntPtr devicePtr, ImGuiIV_DrawingContext ctx)
        {
            // If drawing is not allowed then we return
            if (!allowDrawing)
                return;
            // If radar is off then nothing should be drawn so we just return
            if (IVMenuManager.RadarMode == 0)
                return;
            
            // Draw dashboard UI
            if (playerInCar
                && !(IS_PAUSE_MENU_ACTIVE() || IS_SCREEN_FADING_OUT() || IS_SCREEN_FADED_OUT())
                && !(IS_MESSAGE_BEING_DISPLAYED() && ModSettings.HideWhenSubtitleIsBeingDisplayed)
                && IsCurrentVehicleValid() && IsVehicleTypeValidForDashboard()
                && ModSettings.EnableDashboard)
            {
                ImGuiIV.PushStyleVar(eImGuiStyleVar.WindowBorderSize, 0f);
                ImGuiIV.PushStyleVar(eImGuiStyleVar.WindowRounding, 5f);
                ImGuiIV.PushStyleColor(eImGuiCol.FrameBg, Color.FromArgb(100, Color.Black));

                ImGuiIV.SetNextWindowBgAlpha(0.45f);
                if (ImGuiIV.Begin("##SimpleSpeedometerVehicleDashboard", eImGuiWindowFlags.NoTitleBar | eImGuiWindowFlags.AlwaysAutoResize | eImGuiWindowFlags.NoMove, eImGuiWindowFlagsEx.NoMouseEnable))
                {
                    // - - - DASHBOARD ICONS - - -

                    // Left indicator
                    {
                        Color iconColor = Color.White;

                        if (indicatorLeftOn || hazardLightsOn)
                        {
                            iconColor = mediumBlinking ? Color.Green : Color.White;
                        }

                        ImGuiIV.Image(leftIndicatorTexture, new Vector2(32f), Vector2.Zero, Vector2.One, iconColor, Color.Transparent);
                    }

                    // Engine light
                    if (ModSettings.ShowEngineIcon)
                    {
                        int colorValue = 0;

                        if (IsCurrentVehicleValid())
                        {
                            colorValue = (int)((currentVehicle.EngineHealth * 255f) / 1000f);
                        }

                        if (colorValue <= 0) // Static
                            colorValue = 0;
                        else if (colorValue <= 50) // Blink
                        {
                            colorValue = fastBlinking ? 0 : 255;
                        }

                        ImGuiIV.SameLine();
                        ImGuiIV.Image(engineTexture, new Vector2(48f, 32f), Vector2.Zero, Vector2.One, Color.FromArgb(255, 255, colorValue, colorValue), Color.Transparent);
                    }

                    // ABS light
                    if (ModSettings.ShowABSIcon)
                    {
                        Color iconColor = Color.White;

                        if (IsCurrentVehicleValid())
                        {
                            if (currentVehicle.BrakePedal > 0.5f && (currentVehicle.AbsFlags.Abs || currentVehicle.AbsFlags.AbsAlt))
                            {
                                iconColor = fastBlinking ? Color.Orange : Color.White;
                            }
                        }

                        ImGuiIV.SameLine();
                        ImGuiIV.Image(absTexture, new Vector2(38f, 32f), Vector2.Zero, Vector2.One, iconColor, Color.Transparent);
                    }

                    // Brake light
                    if (ModSettings.ShowParkingBrakeIcon)
                    {
                        if (IsCurrentVehicleValid())
                        {
                            ImGuiIV.SameLine();
                            ImGuiIV.Image(brakeTexture, new Vector2(38f, 32f), Vector2.Zero, Vector2.One, currentVehicle.VehicleFlags.IsHandbrakeOn ? Color.DarkOrange : Color.White, Color.Transparent);
                        }
                    }

                    // Lights
                    if (ModSettings.ShowHeadlightIcon)
                    {
                        Color iconColor = Color.White;

                        if (IsCurrentVehicleValid())
                        {
                            if (currentVehicle.VehicleFlags.LightsOn)
                                iconColor = Color.Green;
                            if (currentVehicle.VehicleFlags2.LongLight)
                                iconColor = Color.Blue;
                        }

                        ImGuiIV.SameLine();
                        ImGuiIV.Image(lightsTexture, new Vector2(32f), Vector2.Zero, Vector2.One, iconColor, Color.Transparent);
                    }

                    // Temperature
                    if (ModSettings.ShowTemperatureIcon)
                    {
                        Color iconColor = Color.White;

                        if (IsCurrentVehicleValid())
                        {
                            if (IS_CAR_IN_WATER(currentVehicle.GetHandle()))
                            {
                                iconColor = fastBlinking ? Color.Blue : Color.White;
                            }
                            else
                            {
                                if (currentVehicle.PetrolTankHealth < 0f || currentVehicle.EngineHealth < 0f)
                                    iconColor = fastBlinking ? Color.Red : Color.White;
                            }
                        }

                        ImGuiIV.SameLine();
                        ImGuiIV.Image(temperatureTexture, new Vector2(38f, 32f), Vector2.Zero, Vector2.One, iconColor, Color.Transparent);
                    }

                    // Cruise Control
                    if (ModSettings.ShowCruiseControlIcon)
                    {
                        Color iconColor = Color.White;

                        if (CruiseControlEnabled)
                            iconColor = CruiseControlTargetSpeed.InBetween(-1f, 1f) ? Color.Orange : Color.Green;

                        ImGuiIV.SameLine();
                        ImGuiIV.Image(cruiseControlTexture, new Vector2(32f, 32f), Vector2.Zero, Vector2.One, iconColor, Color.Transparent);
                    }

                    // TPMS
                    if (ModSettings.ShowTPMSIcon)
                    {
                        if (IsCurrentVehicleValid() && (eVehicleType)currentVehicle.VehicleType == eVehicleType.VEHICLE_TYPE_AUTOMOBILE
                            || (eVehicleType)currentVehicle.VehicleType == eVehicleType.VEHICLE_TYPE_BIKE)
                        {

                            float expectedTirePressure = 1000f * currentVehicle.WheelCount;
                            float currentTirePressure = 0f;

                            // Get pressure
                            for (int i = 0; i < currentVehicle.WheelCount; i++)
                                currentTirePressure += currentVehicle.Wheels[i].TireHealth;

                            Color iconColor = Color.White;
                            bool shouldBlink = false;
                            int blinkMode = 0;

                            // Check blink
                            if (currentTirePressure.InBetween(-1f, 1000f))
                            {
                                shouldBlink = true;
                                blinkMode = 2; // Fast blinking
                            }
                            else if (currentTirePressure.InBetween(1000f, 2000f))
                            {
                                shouldBlink = true;
                                blinkMode = 1; // Medium blinking
                            }

                            // Change icon color
                            if (shouldBlink)
                            {
                                switch (blinkMode)
                                {
                                    case 1: // Medium blinking
                                        iconColor = mediumBlinking ? Color.Orange : Color.White;
                                        break;
                                    case 2: // Fast blinking
                                        iconColor = fastBlinking ? Color.Red : Color.White;
                                        break;
                                }
                            }
                            else
                            {
                                iconColor = currentTirePressure < expectedTirePressure ? Color.Orange : Color.White;
                            }


                            //if (shouldBlink)
                            //{
                            //    if (currentTirePressure <= 50f)
                            //    {
                            //        iconColor = Color.Red;
                            //    }
                            //    else
                            //    {
                            //        iconColor = mediumBlinking ? Color.Orange : Color.White;
                            //    }
                            //}
                            //else
                            //{
                            //    if (currentTirePressure <= 50f)
                            //    {
                            //        iconColor = Color.Red;
                            //    }
                            //    else
                            //    {
                            //        iconColor = currentTirePressure < expectedTirePressure ? Color.Orange : Color.White;
                            //    }
                            //}

                            ImGuiIV.SameLine();
                            ImGuiIV.Image(tpmsTexture, new Vector2(38f, 32f), Vector2.Zero, Vector2.One, iconColor, Color.Transparent);
                        }
                    }

                    // Seatbelt
                    if (ModSettings.EnableSeatbealt && ModSettings.ShowSeatbeltIcon)
                    {
                        if (IsCurrentVehicleValid() && (eVehicleType)currentVehicle.VehicleType == eVehicleType.VEHICLE_TYPE_AUTOMOBILE)
                        {
                            Color iconColor = Color.White;

                            if (playerPed.PedFlags2.WillFlyThroughWindscreen)
                                iconColor = fastBlinking ? Color.Orange : Color.White;

                            ImGuiIV.SameLine();
                            ImGuiIV.Image(seatbeltTexture, new Vector2(23f, 32f), Vector2.Zero, Vector2.One, iconColor, Color.Transparent);
                        }
                    }

                    // Right indicator
                    {
                        Color iconColor = Color.White;

                        if (indicatorRightOn || hazardLightsOn)
                        {
                            iconColor = mediumBlinking ? Color.Green : Color.White;
                        }

                        ImGuiIV.SameLine();
                        ImGuiIV.Image(rightIndicatorTexture, new Vector2(32f), Vector2.Zero, Vector2.One, iconColor, Color.Transparent);
                    }


                    // - - - FUEL - - -
                    if (IsCurrentVehicleValid())
                    {
                        ImGuiIV.Spacing(1);

                        ImGuiIV.TextUnformatted("Fuel");
                        ImGuiIV.ProgressBar(currentVehicle.PetrolTankHealth / 1000.0f);
                    }

                    // - - - SPEED STUFF - - -
                    if (IsCurrentVehicleValid())
                    {
                        ImGuiIV.Spacing(1);

                        // Current Gear
                        {
                            uint driveGears = currentVehicle.Handling.DriveGears;
                            for (int i = 0; i < driveGears + 1; i++)
                            {
                                Color color = Color.White;

                                if (currentVehicle.CurrentGear == i)
                                {
                                    color = Color.Red;

                                    // Blink number if car should shift now
                                    if (currentVehicle.EngineRPM > 0.87f)
                                    {
                                        color = fastBlinking ? Color.White : Color.Red;
                                    }
                                }

                                ImGuiIV.TextColored(color, i == 0 ? "N" : i.ToString());

                                if (i != driveGears)
                                    ImGuiIV.SameLine();
                            }

                            ImGuiIV.SameLine();
                        }

                        // Current Speed
                        {
                            string speedString = "0 MPH";

                            if (ModSettings.UseMPH)
                            {
                                if (CruiseControlEnabled)
                                    speedString = string.Format("{0} MPH (Target: {1} MPH)", (int)currentVehicleSpeed, (int)(CruiseControlTargetSpeed * 2.237f));
                                else
                                    speedString = string.Format("{0} MPH", (int)currentVehicleSpeed);
                            }
                            else
                            {
                                if (CruiseControlEnabled)
                                    speedString = string.Format("{0} KM/H (Target: {1} KM/H)", (int)currentVehicleSpeed, (int)(CruiseControlTargetSpeed * 3.6f));
                                else
                                    speedString = string.Format("{0} KM/H", (int)currentVehicleSpeed);
                            }
                            Vector2 textSize = ImGuiIV.CalcTextSize(speedString);

                            // Calculate the position for the text to be right-aligned
                            float offsetX = ImGuiIV.GetWindowSize().X - textSize.X - ImGuiIV.GetStyle().ItemSpacing.X;

                            // Position the text
                            ImGuiIV.SetCursorPosX(offsetX);

                            ImGuiIV.TextUnformatted(speedString);
                        }
                    }


                    // Set window pos
                    RectangleF rect = IVGame.GetRadarRectangle();
                    ImGuiIV.SetWindowPos(new Vector2(rect.Right, rect.Y) + ModSettings.DashboardOffset);

                }
                ImGuiIV.End();

                ImGuiIV.PopStyleVar(2);
                ImGuiIV.PopStyleColor();
            }

            if (!ModSettings.EnableSpeedometer)
                return;

            // Fading stuff
            CalculateTextureFadingValues();

            // Draw digits and needle
            if (!isFadedOut)
            {
                // Get rotation value for the needle based on the current vehicle speed
                float rotation = 0f;

                if (ModSettings.UseMPH)
                    rotation = (Helper.DegreeToRadian(currentVehicleSpeed) / 150f) * 228f; // Inaccurate at higher speeds
                else
                    rotation = Helper.DegreeToRadian(currentVehicleSpeed);

                // Gets the radar rectangle
                RectangleF rect = IVGame.GetRadarRectangle();

                ctx.AddImage(digitsTexture,         new RectangleF(rect.X - digitsAndNeedleOffsetX, rect.Y - digitsAndNeedleOffsetY, rect.Width + digitsAndNeedleSizeWidth, rect.Height + digitsAndNeedleSizeHeight), Color.FromArgb(textureAlphaValue, Color.White));
                ctx.AddImageRotated(needleTexture,  new RectangleF(rect.X - digitsAndNeedleOffsetX, rect.Y - digitsAndNeedleOffsetY, rect.Width + digitsAndNeedleSizeWidth, rect.Height + digitsAndNeedleSizeHeight), rotation, Color.FromArgb(textureAlphaValue, Color.White));
            }
        }

        private void Main_ProcessPad(UIntPtr padPtr)
        {
            if (padPtr == UIntPtr.Zero)
                return;

            IVPad pad = IVPad.FromUIntPtr(padPtr);

            // Check brake key
            if (pad.Values[(int)ePadControls.INPUT_VEH_BRAKE].CurrentValue != 0)
            {
                // Disable cruise control
                CruiseControlEnabled = false;
            }

            // Check left indicator key
            if (pad.Values[(int)ePadControls.INPUT_FRONTEND_LEFT].CurrentValue == 255)
            {
                if (!leftIndicatorKeyWatch.IsRunning)
                    leftIndicatorKeyWatch.Start();
            }
            else
            {
                if (leftIndicatorKeyWatch.IsRunning)
                    leftIndicatorKeyWatch.Reset();
            }

            // Check right indicator key
            if (pad.Values[(int)ePadControls.INPUT_FRONTEND_RIGHT].CurrentValue == 255)
            {
                if (!rightIndicatorKeyWatch.IsRunning)
                    rightIndicatorKeyWatch.Start();
            }
            else
            {
                if (rightIndicatorKeyWatch.IsRunning)
                    rightIndicatorKeyWatch.Reset();
            }
        }
        private void Main_ProcessAutomobile(UIntPtr vehPtr)
        {
            if (vehPtr == UIntPtr.Zero)
                return;
            if (!IsCurrentVehicleValid())
                return;

            if (vehPtr == currentVehicle.GetUIntPtr())
            {
                if (CruiseControlEnabled)
                {
                    if (CruiseControlTargetSpeed.InBetween(-1f, 1f))
                        return;

                    float speedError = (CruiseControlTargetSpeed * cruiseControlTargetSpeedMultiplier) - currentVehicleSpeedRaw;

                    if (speedError > 0)
                    {
                        currentVehicle.GasPedal = Math.Min(1.0f, speedError * CruiseControlGasPedalSensitivity);
                        currentVehicle.BrakePedal = 0.0f;
                    }
                    else
                    {
                        currentVehicle.GasPedal = 0.0f;
                        currentVehicle.BrakePedal = Math.Min(1.0f, -speedError * CruiseControlBrakePedalSensitivity);
                    }
                }
            }
        }

        private void Main_Tick(object sender, EventArgs e)
        {
            // If is mp session then reset stuff and return
            if (IVNetwork.IsNetworkGameRunning() && ModSettings.DisableInMP)
            {
                Reset();
                return;
            }
            
            if (IS_PAUSE_MENU_ACTIVE())
                return;

            isUsingController = IS_USING_CONTROLLER();

            // Get the player ped
            playerPed = IVPed.FromUIntPtr(IVPlayerInfo.FindThePlayerPed());

            // Get current vehicle of player
            currentVehicle = IVVehicle.FromUIntPtr(playerPed.GetVehicle());

            if (currentVehicle != null)
            {
                previousVehicle = currentVehicle;

                wasSeatbeltReset = false;

                // Set player is in car
                playerInCar = true;

                // Get pad for control stuff
                IVPad pad = IVPad.GetPad();

                // Toggle stuff and other features that only the driver can do
                if (currentVehicle.VehicleFlags.EngineOn)
                {
                    if (currentVehicle.IsDriver(playerPed))
                    {
                        // Hazard lights toggle
                        if (ImGuiHelper.IsKeyPressed(ModSettings.HazardLightToggleKey, false) || ImGuiHelper.IsKeyPressed(ModSettings.HazardLightToggleKeyAlt, false))
                        {
                            // Toggle hazard lights
                            hazardLightsOn = !hazardLightsOn;

                            // Disable indicator lights if hazard lights are activated
                            if (hazardLightsOn)
                            {
                                indicatorLeftOn = false;
                                indicatorRightOn = false;
                            }
                        }

                        // Left/Right indicators toggle
                        // Can only toggle indicators when the hazard lights are not enabled
                        if (!hazardLightsOn)
                        {
                            if (isUsingController)
                            {
                                // Switch left/right indicators
                                if (leftIndicatorKeyWatch.Elapsed.TotalSeconds > 0.5d && ImGuiIV.IsKeyPressed(eImGuiKey.ImGuiKey_GamepadFaceDown, false))
                                {
                                    indicatorLeftOn = !indicatorLeftOn;

                                    // Turn off right indicator when enabled
                                    if (indicatorLeftOn && indicatorRightOn)
                                        indicatorRightOn = false;
                                }
                                if (rightIndicatorKeyWatch.Elapsed.TotalSeconds > 0.5d && ImGuiIV.IsKeyPressed(eImGuiKey.ImGuiKey_GamepadFaceDown, false))
                                {
                                    indicatorRightOn = !indicatorRightOn;

                                    // Turn off left indicator when enabled
                                    if (indicatorRightOn && indicatorLeftOn)
                                        indicatorLeftOn = false;
                                }

                                // Disable indicators
                                if (ImGuiIV.IsKeyPressed(eImGuiKey.ImGuiKey_GamepadFaceDown)
                                    && !(leftIndicatorKeyWatch.Elapsed.TotalSeconds > 0.5d || rightIndicatorKeyWatch.Elapsed.TotalSeconds > 0.5d))
                                {
                                    indicatorLeftOn = false;
                                    indicatorRightOn = false;
                                }
                            }
                            else
                            {
                                byte leftSteerValue =   pad.Values[(int)ePadControls.INPUT_VEH_MOVE_LEFT].CurrentValue;
                                byte rightSteerValue =  pad.Values[(int)ePadControls.INPUT_VEH_MOVE_RIGHT].CurrentValue;

                                bool keyForLeftIndicatorPressed =   ImGuiHelper.IsKeyPressed(ModSettings.LeftIndicatorToggleKey, false) || ImGuiHelper.IsKeyPressed(ModSettings.LeftIndicatorToggleAltKey, false);
                                bool keyForRightIndicatorPressed =  ImGuiHelper.IsKeyPressed(ModSettings.RightIndicatorToggleKey, false) || ImGuiHelper.IsKeyPressed(ModSettings.RightIndicatorToggleAltKey, false);

                                // Switch left indicator
                                if (keyForLeftIndicatorPressed)
                                {
                                    indicatorLeftOn = !indicatorLeftOn;

                                    // Turn off right indicator when enabled
                                    if (indicatorLeftOn && indicatorRightOn)
                                        indicatorRightOn = false;
                                }

                                // Switch right indicator
                                if (keyForRightIndicatorPressed)
                                {
                                    indicatorRightOn = !indicatorRightOn;

                                    // Turn off left indicator when enabled
                                    if (indicatorRightOn && indicatorLeftOn)
                                        indicatorLeftOn = false;
                                }

                                // Disable indicators
                                if (!keyForLeftIndicatorPressed && !keyForRightIndicatorPressed)
                                {
                                    if (ImGuiIV.IsKeyPressed(eImGuiKey.ImGuiKey_LeftShift) && !(leftSteerValue == 255 || rightSteerValue == 255))
                                    {
                                        indicatorLeftOn = false;
                                        indicatorRightOn = false;
                                    }
                                }
                            }
                        }

                        // Cruise control toggle
                        if (ImGuiHelper.IsKeyPressed(ModSettings.CruiseControlToggleKey, false))
                        {
                            CruiseControlEnabled = !CruiseControlEnabled;

                            if (!ModSettings.EnableDashboard)
                            {
                                if (CruiseControlEnabled)
                                {
                                    if (CruiseControlTargetSpeed.InBetween(0f, 1f))
                                    {
                                        CruiseControlEnabled = false;
                                        ShowSubtitleMessage("Cruise control cannot be enabled because the target speed was not set yet or is too low.", 3000);
                                    }
                                    else
                                        ShowSubtitleMessage("Cruise control enabled.");
                                }
                                else
                                {
                                    ShowSubtitleMessage("Cruise control disabled.");
                                }
                            }
                        }

                        // Cruise control set target speed
                        if (ImGuiHelper.IsKeyPressed(ModSettings.CruiseControlSetTargetSpeedKey, false))
                        {
                            CruiseControlTargetSpeed = currentVehicleSpeedRaw;

                            if (CruiseControlTargetSpeed.InBetween(-1f, 1f))
                            {
                                if (!ModSettings.EnableDashboard)
                                {
                                    CruiseControlEnabled = false;
                                    ShowSubtitleMessage("Please set a higher target speed for cruise control. Cruise control got disabled.", 3500);
                                }
                                else
                                {
                                    ShowSubtitleMessage("Please set a higher target speed for cruise control.", 3000);
                                }
                            }
                            else
                            {
                                if (!ModSettings.EnableDashboard)
                                {
                                    ShowSubtitleMessage("Cruise control target speed set to {0} {1}.", (int)currentVehicleSpeed, ModSettings.UseMPH ? "MPH" : "KM/H");
                                }
                            }
                        }

                        // Seatbelt
                        if (!IVNetwork.IsNetworkSession() && ModSettings.EnableSeatbealt)
                        {
                            if (ModSettings.PutSeatbeltOnAutomatically)
                            {
                                playerPed.PedFlags2.WillFlyThroughWindscreen = true;
                            }
                            else
                            {
                                if (ImGuiHelper.IsKeyPressed(ModSettings.SeatbealtToggleKey, false))
                                {
                                    playerPed.PedFlags2.WillFlyThroughWindscreen = !playerPed.PedFlags2.WillFlyThroughWindscreen;
                                }
                            }
                        }
                    }
                }
                else
                {
                    hazardLightsOn = false;
                    indicatorLeftOn = false;
                    indicatorRightOn = false;
                }

                // If the game currently has control over the hazard lights, we turn them off and give the script the control over them.
                if (currentVehicle.VehicleFlags3.HazardLights)
                {
                    currentVehicle.VehicleFlags3.HazardLights = false;
                    hazardLightsOn = true;
                }

                // Set vehicle left/right indicator state
                if (indicatorLeftOn || hazardLightsOn)
                {
                    currentVehicle.VehicleFlags3.LeftIndicator = mediumBlinking;
                }
                else
                {
                    currentVehicle.VehicleFlags3.LeftIndicator = false;
                }
                if (indicatorRightOn || hazardLightsOn)
                {
                    currentVehicle.VehicleFlags3.RightIndicator = mediumBlinking;
                }
                else
                {
                    currentVehicle.VehicleFlags3.RightIndicator = false;
                }

                // Process additional features
                CheckFuelStuff();
                CheckCruiseControlStuff();

                // Get the vehicle handle of the current vehicle the player is in
                int handle = currentVehicle.GetHandle();

                // Get the speed of the vehicle
                GET_CAR_SPEED(handle, out currentVehicleSpeedRaw);

                // Convert speed to MPH/KMH based on the ini setting
                currentVehicleSpeed = currentVehicleSpeedRaw * (ModSettings.UseMPH ? 2.23694f : 3.6f);

                // Allow drawing the digits and pin textures
                allowDrawing = true;
            }
            else
            {
                // Set player is not in car anymore
                playerInCar = false;

                // Set hazard lights if they where toggled when exiting the last vehicle
                if (previousVehicle != null)
                {
                    if (hazardLightsOn)
                        previousVehicle.VehicleFlags3.HazardLights = true;
                }

                // If texture is faded out then we can reset some stuff
                if (isFadedOut)
                    Reset();
            }
        }

        private object Main_ScriptCommandReceived(Script fromScript, object[] args, string command)
        {
            switch (command.ToLower())
            {
                // Hazard lights and indicators
                case "get_hazard_lights":    return hazardLightsOn;
                case "get_left_indicator":   return indicatorLeftOn;
                case "get_right_indicator":  return indicatorRightOn;

                case "set_hazard_lights":
                    hazardLightsOn = Convert.ToBoolean(args[0]);
                    return true;
                case "set_left_indicator":
                    indicatorLeftOn = Convert.ToBoolean(args[0]);
                    return true;
                case "set_right_indicator":
                    indicatorRightOn = Convert.ToBoolean(args[0]);
                    return true;

                // Gas prices
                case "get_ron_price_per_liter":         return RonPricePerLiter;
                case "get_globeoil_price_per_liter":    return GlobeOilPricePerLiter;
                case "get_terroil_price_per_liter":     return TerrOilPricePerLiter;

                case "set_ron_price_per_liter":
                    RonPricePerLiter = Convert.ToInt32(args[0]);
                    return true;
                case "set_globeoil_price_per_liter":
                    GlobeOilPricePerLiter = Convert.ToInt32(args[0]);
                    return true;
                case "set_terroil_price_per_liter":
                    TerrOilPricePerLiter = Convert.ToInt32(args[0]);
                    return true;

                // Gas station
                case "get_current_gas_station": return (int)currentGasStation;
            }

            return null;
        }

    }
}
