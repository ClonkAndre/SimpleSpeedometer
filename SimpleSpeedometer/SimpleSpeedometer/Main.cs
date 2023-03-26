using System;
using System.Drawing;
using System.IO;

using IVSDKDotNet;
using IVSDKDotNet.Direct3D9;
using static IVSDKDotNet.Native.Natives;

namespace SimpleSpeedometer {
    public class Main : Script {

        #region Variables
        private D3DGraphics gfx;
        private D3DResource digitsTexture;
        private D3DResource pinTexture;

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
        }
        #endregion

        private void Main_Initialized(object sender, EventArgs e)
        {
            // Create a new D3DGraphics object for this Script.
            gfx = new D3DGraphics(this);
            gfx.OnInit += Gfx_OnInit;
            gfx.OnDeviceEndScene += Gfx_OnDeviceEndScene;

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

        private void Gfx_OnInit(IntPtr device)
        {
            string digitsFilePath = string.Format("{0}\\{1}\\digits.png", ScriptResourceFolder, skin);
            string pinFilePath = string.Format("{0}\\{1}\\pin.png", ScriptResourceFolder, skin);

            // Check if both files exists
            if (!File.Exists(digitsFilePath) || !File.Exists(pinFilePath))
            {
                CGame.Console.PrintWarning(string.Format("[SimpleSpeedometer] Please make sure that 'digits.png' and 'pin.png' exists in the SimpleSpeedometer\\{0} folder. Mod will be aborted.", skin));
                Abort();
                return;
            }

            // Create digits texture
            D3DResult r = gfx.CreateD3D9Texture(device, digitsFilePath);
            if (r.Error != null)
            {
                CGame.Console.PrintError(string.Format("[SimpleSpeedometer] An error occured while trying to create 'digits' texture! Details: {0}", r.Error.ToString()));
            }
            else
            {
                digitsTexture = r.DXObject as D3DResource;
            }

            // Create pin texture
            r = gfx.CreateD3D9Texture(device, pinFilePath);
            if (r.Error != null)
            {
                CGame.Console.PrintError(string.Format("[SimpleSpeedometer] An error occured while trying to create 'pin' texture! Details: {0}", r.Error.ToString()));
            }
            else
            {
                pinTexture = r.DXObject as D3DResource;
            }
        }
        private void Gfx_OnDeviceEndScene(IntPtr device)
        {
            // If drawing is not allowed then return out of this method.
            if (!allowDrawing)
                return;
            // If radar is off then return out if this method.
            if (CMenuManager.Display.RadarMode == 0)
                return;

            // Get rotation value for the pin based on the current vehicle speed
            float rot = speed * ((float)Math.PI / 112.5f);

            // Gets the radar rectangle
            RectangleF rect = CGame.GetRadarRectangle();

            // Fading
            if (IS_PAUSE_MENU_ACTIVE())
            {
                // Force fade textures out for pause menu
                if (doFading)
                {
                    if (!(texturesAlpha <= 0))
                        texturesAlpha -= fadingOutSpeed;
                    if (texturesAlpha < 0)
                        texturesAlpha = 0;
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
                        if (texturesAlpha > 255)
                            texturesAlpha = 255;
                    }
                    else
                    {
                        texturesAlpha = 255;
                    }
                }
            }

            // Draw digits and pin
            if (!(texturesAlpha <= 0))
            {
                gfx.DrawTexture(device, digitsTexture, new RectangleF(rect.X - digitsAndPinOffsetX, rect.Y - digitsAndPinOffsetY, rect.Width + digitsAndPinSizeWidth, rect.Height + digitsAndPinSizeHeight), Color.FromArgb(texturesAlpha, Color.White));
                gfx.DrawTexture(device, pinTexture, new RectangleF(rect.X - digitsAndPinOffsetX, rect.Y - digitsAndPinOffsetY, rect.Width + digitsAndPinSizeWidth, rect.Height + digitsAndPinSizeHeight), rot, Color.FromArgb(texturesAlpha, Color.White));
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
            CVehicle veh = CPed.FromPointer(CPlayerInfo.FindPlayerPed()).GetVehicle();
            if (veh != null)
            {
                // Get the vehicle handle of the current vehicle the player is in
                uint handle = CPools.GetVehiclePool().GetIndex(veh.GetUIntPtr());

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
                        texturesAlpha -= fadingOutSpeed;
                    else
                        texturesAlpha = 0;
                }
                else
                {
                    // Set variable to 0 just in case it is below 0
                    texturesAlpha = 0;

                    // Set the speed to 0 if the player is not currently in a vehicle
                    speed = 0f;

                    // Do not allow drawing when player is not in a vehicle
                    allowDrawing = false;
                }
            }
        }

    }
}
