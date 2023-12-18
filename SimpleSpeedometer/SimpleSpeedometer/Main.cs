using System;
using System.Drawing;
using System.IO;

using IVSDKDotNet;
using static IVSDKDotNet.Native.Natives;

namespace SimpleSpeedometer
{
    public class Main : Script
    {

        #region Variables
        private IntPtr digitsTexture;
        private IntPtr pinTexture;

        private bool allowDrawing, disableInMP, playerInCar;
        private int texturesAlpha;

        private bool useMPH, doFading;
        private string skin;
        private int fadingInSpeed, fadingOutSpeed;
        private float digitsAndPinSizeWidth, digitsAndPinSizeHeight;
        private float digitsAndPinOffsetX, digitsAndPinOffsetY;
        private float speed;
        #endregion

        #region Constructor
        public Main()
        {
            Initialized += Main_Initialized;
            Tick += Main_Tick;
            OnFirstD3D9Frame += Main_OnFirstD3D9Frame;
        }
        #endregion

        private void Main_Initialized(object sender, EventArgs e)
        {
            // Load settings file and get things from the settings file
            if (Settings.Load())
            {
                // Main
                useMPH = Settings.GetBoolean("Main", "UseMPH", true);
                disableInMP = Settings.GetBoolean("Main", "DisableInMP", false);

                // Style
                skin = Settings.GetValue("Style", "Skin", "Default");
                doFading = Settings.GetBoolean("Style", "DoFading", true);
                fadingInSpeed = Settings.GetInteger("Style", "FadingInSpeed", 4);
                fadingOutSpeed = Settings.GetInteger("Style", "FadingOutSpeed", 6);

                // Size
                digitsAndPinSizeWidth = Settings.GetFloat("Size", "DigitsAndPinSizeWidth", 128f);
                digitsAndPinSizeHeight = Settings.GetFloat("Size", "DigitsAndPinSizeHeight", 128f);

                // Offset
                digitsAndPinOffsetX = Settings.GetFloat("Offset", "DigitsAndPinOffsetX", 64f);
                digitsAndPinOffsetY = Settings.GetFloat("Offset", "DigitsAndPinOffsetY", 64f);
            }
        }

        private void Main_Tick(object sender, EventArgs e)
        {
            // If is mp session then reset stuff and return
            if (IS_NETWORK_SESSION() && disableInMP)
            {
                allowDrawing = false;
                return;
            }

            // Get current vehicle of player
            IVVehicle veh = IVVehicle.FromUIntPtr(IVPed.FromUIntPtr(IVPlayerInfo.FindThePlayerPed()).GetVehicle());
            if (veh != null)
            {
                // Get the vehicle handle of the current vehicle the player is in
                uint handle = IVPools.GetVehiclePool().GetIndex(veh.GetUIntPtr());

                // Get the speed of the vehicle
                GET_CAR_SPEED((int)handle, out speed);

                // Convert speed to MPH/KMH based on the ini setting
                speed = speed * (useMPH ? 2.3f : 3.6f);

                // Allow drawing the digits and pin textures
                allowDrawing = true;
                playerInCar = true;
            }
            else
            {
                // Set player is not in car anymore
                playerInCar = false;

                // Force fade textures out
                if (!(texturesAlpha <= 0))
                {
                    if (doFading)
                    {
                        int newAlpha = texturesAlpha;
                        newAlpha -= fadingOutSpeed;

                        // Prevent alpha value from being under 0 and above 255
                        if (newAlpha < 0)
                            newAlpha = 0;

                        texturesAlpha = newAlpha;
                    }
                    else
                        texturesAlpha = 0;
                }
                else
                {
                    // Set the speed to 0 if the player is not currently in a vehicle
                    speed = 0f;

                    // Do not allow drawing when player is not in a vehicle
                    allowDrawing = false;
                }
            }
        }

        private void Main_OnFirstD3D9Frame(object sender, EventArgs e)
        {
            ImGuiIV.AddDrawCommand(this, () =>
            {
                if (ImGuiIV.BeginCanvas(this, out ImGuiIV_DrawingContext ctx))
                {
                    // If drawing is not allowed then return out of this method.
                    if (!allowDrawing)
                    {
                        ImGuiIV.EndCanvas();
                        return;
                    }
                    // If radar is off then return out if this method.
                    if (IVMenuManager.RadarMode == 0)
                    {
                        ImGuiIV.EndCanvas();
                        return;
                    }

                    // Get rotation value for the pin based on the current vehicle speed
                    float rot = speed * ((float)Math.PI / 112.5f);

                    // Gets the radar rectangle
                    RectangleF rect = IVGame.GetRadarRectangle();

                    // Fading
                    if (IS_PAUSE_MENU_ACTIVE())
                    {
                        // Force fade textures out for pause menu
                        if (doFading)
                        {
                            if (!(texturesAlpha <= 0))
                                texturesAlpha -= fadingOutSpeed;
                        }
                        else
                        {
                            texturesAlpha = 0;
                        }
                    }
                    else
                    {
                        // Fade textures in only if player is in car
                        if (playerInCar)
                        {
                            if (doFading)
                            {
                                if (!(texturesAlpha >= 255))
                                    texturesAlpha += fadingInSpeed;
                            }
                            else
                            {
                                texturesAlpha = 255;
                            }
                        }
                    }

                    // Prevent alpha value from being under 0 and above 255
                    if (texturesAlpha < 0)
                        texturesAlpha = 0;
                    if (texturesAlpha > 255)
                        texturesAlpha = 255;

                    // Draw digits and pin
                    if (!(texturesAlpha <= 0))
                    {
                        ctx.AddImage(digitsTexture, new RectangleF(rect.X - digitsAndPinOffsetX, rect.Y - digitsAndPinOffsetY, rect.Width + digitsAndPinSizeWidth, rect.Height + digitsAndPinSizeHeight), Color.FromArgb(texturesAlpha, Color.White));
                        ctx.AddImageRotated(pinTexture, new RectangleF(rect.X - digitsAndPinOffsetX, rect.Y - digitsAndPinOffsetY, rect.Width + digitsAndPinSizeWidth, rect.Height + digitsAndPinSizeHeight), rot, Color.FromArgb(texturesAlpha, Color.White));
                    }

                    ImGuiIV.EndCanvas();
                }
            });

            string digitsFilePath = string.Format("{0}\\{1}\\digits.dds", ScriptResourceFolder, skin);
            string pinFilePath = string.Format("{0}\\{1}\\pin.dds", ScriptResourceFolder, skin);

            // Check if both files exists
            if (!File.Exists(digitsFilePath) || !File.Exists(pinFilePath))
            {
                IVGame.Console.PrintWarning(string.Format("[SimpleSpeedometer] Please make sure that 'digits.dds' and 'pin.dds' exists in the SimpleSpeedometer\\{0} folder. Script will be aborted.", skin));
                Abort();
                return;
            }

            // Create digits texture
            if (ImGuiIV.CreateTextureFromFile(this, digitsFilePath, out IntPtr ptr, out int width, out int height))
                digitsTexture = ptr;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create 'digits' texture!");

            // Create pin texture
            if (ImGuiIV.CreateTextureFromFile(this, pinFilePath, out IntPtr ptr2, out int width2, out int height2))
                pinTexture = ptr2;
            else
                IVGame.Console.PrintError("[SimpleSpeedometer] Failed to create 'pin' texture!");
        }

    }
}
