using LSPD_First_Response.Mod.API;
using LSPD_First_Response.Mod.Callouts;
using MaxPlayle.OneFileCallouts.Callouts;
using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MaxPlayle.OneFileCallouts
{
    #region Main Stuff
    internal class PluginMain : Plugin
    {
        internal const string ProjectName = "OneFileCallouts";

        /// <summary>
        /// Initialise all code within this method
        /// </summary>
        public override void Initialize()
        {
            // Add all console commands defined within the plugin.
            Game.AddConsoleCommands();

            Log.Info($"------------------------- {ProjectName} -------------------------");
            Log.Info($"Version: {Assembly.GetAssembly(typeof(PluginMain)).GetName().Version}");
            Log.Info("Created in a livestream as a joke! But hey, who knows what happened?");
            Log.Info($"----------------------------------------------------------------");

            Functions.OnOnDutyStateChanged += RegisterCallouts;

        }

        private void RegisterCallouts(bool onDuty)
        {
            if (onDuty)
            {
                Functions.RegisterCallout(typeof(StolenVehicle));
            }
        }

        /// <summary>
        /// Clean up everything we've done
        /// </summary>
        public override void Finally()
        {
         
        }

    }

    internal class Log
    {
        public static void Info(string info) => Game.LogTrivial($"[{PluginMain.ProjectName}] [TRACE] {info}");
        public static void Warn(string warn) => Game.LogTrivial($"[{PluginMain.ProjectName}] [WARN] {warn}");
        public static void Error(string error) => Game.LogTrivial($"[{PluginMain.ProjectName}] [ERROR] {error}");
        public static void Error(Exception e)
        {
            Error("--------------- ONE LINE CALLOUTS EXCEPTION ---------------");
            Error(e.GetType().Name);
            Error(e.StackTrace);
            Error(e.Message);
            Error("-----------------------------------------------------------");
        }

    }

    internal class OFCallout : Callout
    {

        public Ped Player => Game.LocalPlayer.Character;
        public Player LocalPlayer => Game.LocalPlayer;

        public bool CalloutRunning = false;
        public bool CalloutFinished = false;

        protected Ped Suspect;
        protected Blip SuspectBlip;
        protected Blip SearchArea;
        protected Vehicle SuspectVehicle;

        public override void End()
        {
            Log.Info("** CALLOUT IS ENDING ** Stack trace:\n" + Environment.StackTrace);


            base.End();
        }

    }

    internal class CalloutHelper
    {

        public static T SpawnOrRetry<T>(Func<T> constructor, int maxTries) where T : Entity
        {
            for (int i = 0; i < maxTries; i++)
            {
                Log.Info($"Attempting to spawn {typeof(T).Name} attempt {i}/{maxTries}....");

                var entity = constructor();

                if ((bool)entity)
                    return entity;
                else
                    // Make elegible for garbage collection
                    entity = null;
            }

            // failed to spawn
            Log.Warn("** FAILED TO SPAWN ENTITY ** It's probably a good idea to restart your game!");
            return null;
        }

    }


    internal static class ExtensionMethods
    {
        public static void MakeMissionPed(this Ped ped)
        {
            ped.IsPersistent = true;
            ped.BlockPermanentEvents = true;
        }

        public static void MakeMissionVehicle(this Vehicle vehicle)
        {
            vehicle.IsPersistent = true;
        }
    
    }


    #endregion
}

namespace MaxPlayle.OneFileCallouts.Callouts
{
    [CalloutInfo("StolenVehicleCallout", CalloutProbability.Medium)]
    internal class StolenVehicle : OFCallout
    {

        bool IsPursuitCreated = false;
        LHandle Pursuit;

        public override bool OnBeforeCalloutDisplayed()
        {

            CalloutMessage = "Stolen Vehicle";
            CalloutAdvisory = "A vehicle has been stolen nearby. Please track it down and apprehend the suspects!";

            while (true)
            {
                GameFiber.Yield();

                Vector3 pos = World.GetNextPositionOnStreet(Player.Position.Around(300f, 600f));
                if (pos.DistanceTo(Player.Position) > 200f)
                {
                    CalloutPosition = pos;
                    break;
                }
            }

            Log.Info("pos: " + this.CalloutPosition.ToString());

            ShowCalloutAreaBlipBeforeAccepting(CalloutPosition, 100f);

            return base.OnBeforeCalloutDisplayed();
        }

        public override bool OnCalloutAccepted()
        {
            CalloutRunning = true;

            // marks the callout as running in lspdfr
            base.OnCalloutAccepted();

            GameFiber.StartNew(this.MissionScript, "OneFileCallouts MissionScript Thread");

            return true;
        }

        void MissionScript()
        {
            try
            {

                SuspectVehicle = CalloutHelper.SpawnOrRetry<Vehicle>(() => new Vehicle(CalloutPosition, 10f), 5);

                if (!SuspectVehicle)
                    throw new Exception("Couldn't spawn suspect vehicle.");

                SuspectVehicle.MakeMissionVehicle();

                Suspect = SuspectVehicle.CreateRandomDriver();
                if (!Suspect)
                    throw new Exception("Couldn't spawn suspect.");

                Suspect.MakeMissionPed();

                // Drive erratically 
                Suspect.Tasks.CruiseWithVehicle(18f, VehicleDrivingFlags.Emergency);

                SearchArea = new Blip(Suspect.Position.Around(50f), 150f)
                {
                    Color = Color.Red,
                    Name = "Search Area",
                    Alpha = 0.7f,
                    IsFriendly = false,
                    IsRouteEnabled = true,
                };

                if (!SearchArea)
                    throw new Exception("Couldn't spawn Search Area");

                while (CalloutRunning)
                {
                    GameFiber.Yield();

                    if (IsPursuitCreated)
                        break;
                }

                // Pursuit will have been created.

                while (CalloutRunning)
                {
                    GameFiber.Yield();

                    if (Suspect && !Functions.IsPedArrested(Suspect))
                    {
                        Game.DisplaySubtitle("Apprehend the ~r~Suspect.");
                    }

                    if (Suspect && Functions.IsPedArrested(Suspect))
                        break;
                }

                CalloutFinished = true;
                End();
            }
            catch (ThreadAbortException e)
            {
                Game.DisplayNotification("Something forced this callout to stop!");
                End();
            }
            catch (Exception e)
            {
                Log.Error(e);
                End();
            }
        }

        public override void Process()
        {
            try
            {
                base.Process();

                if (!Suspect)
                    throw new Exception("Suspect no longer exists.");

                if (!SuspectVehicle)
                    throw new Exception("Suspect Vehicle no longer exists.");

                if (!IsPursuitCreated && SuspectVehicle.DistanceTo(Player) < 40f)
                {
                    Pursuit = Functions.CreatePursuit();

                    Functions.AddPedToPursuit(Pursuit, Suspect);
                    Functions.SetPursuitIsActiveForPlayer(Pursuit, true);
                    Functions.AddCopToPursuit(Pursuit, Player);

                    IsPursuitCreated = true;
                }

            }
            catch (ThreadAbortException)
            {
                Log.Error("** SOMETHING FORCED THE CALLOUT TO STOP **");
                End();
            }
            catch (Exception e)
            {
                End();
                Log.Error(e);
            }
        }

        public override void End() 
        {
            base.End();

            if (CalloutFinished)
            {
                Log.Info("Softly deleting objects");
                if (Suspect)
                    Suspect.Dismiss();
                
                if (SuspectVehicle)
                    SuspectVehicle.Dismiss();
            }
            else
            {
                Log.Info("Forcibly deleting objects");
                if (Suspect)
                    Suspect.Delete();
                
                if (SuspectVehicle)
                    SuspectVehicle.Delete();
            }

            SuspectBlip.Delete();
            SearchArea.Delete();
        }
    }
}
