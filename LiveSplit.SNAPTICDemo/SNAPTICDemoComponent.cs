using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using System;
using System.Windows.Forms;
using System.Xml;

namespace LiveSplit.SNAPTICDemo
{
    public sealed class SNAPTICDemoComponent : LogicComponent
    {
        private readonly LiveSplitState state;
        private readonly TimerModel timer;
        private readonly GodotMemoryReader memory;
        private readonly SNAPTICDemoSettings settings;
        private bool endSplitCompleted;
        private int guardedAttemptCount = -1;
        private int guardedSplitIndex = -1;

        public override string ComponentName => "SNAPTIC Demo Autosplitter";

        public SNAPTICDemoComponent(LiveSplitState state)
        {
            this.state = state;
            SNAPTICDemoLog.Initialize();
            SNAPTICDemoLog.Write("Component created.");
            timer = new TimerModel { CurrentState = state };
            memory = new GodotMemoryReader();
            settings = new SNAPTICDemoSettings();
        }

        public override Control GetSettingsControl(LayoutMode mode) => settings;
        public override XmlNode GetSettings(XmlDocument document) => settings.GetSettings(document);
        public override void SetSettings(XmlNode node)
        {
            settings.SetSettings(node);
            SNAPTICDemoLog.Write(
                "Settings loaded: Start=" + settings.Start +
                ", Split=" + settings.Split +
                ", EndSplit=" + settings.EndSplit +
                ", Reset=" + settings.Reset + ".");
        }

        public override void Update(
            IInvalidator invalidator,
            LiveSplitState liveSplitState,
            float width,
            float height,
            LayoutMode mode)
        {
            memory.Update();

            bool start = memory.ConsumeStartSignal();
            bool reset = memory.ConsumeResetSignal();

            if (start)
                SNAPTICDemoLog.Write("Memory reader produced START signal. Timer phase=" + state.CurrentPhase + ".");
            if (reset)
                SNAPTICDemoLog.Write("Memory reader produced RESET signal. Timer phase=" + state.CurrentPhase + ".");

            int attemptCount = state.Run?.AttemptCount ?? -1;
            if (endSplitCompleted &&
                (attemptCount != guardedAttemptCount ||
                 state.CurrentSplitIndex != guardedSplitIndex))
            {
                endSplitCompleted = false;
            }

            if (state.CurrentPhase == TimerPhase.NotRunning)
            {
                memory.SetEndSplitTrackingEnabled(true);
                endSplitCompleted = false;
                guardedAttemptCount = -1;
                guardedSplitIndex = -1;
                memory.ConsumeEndSplitSignal();
                if (start)
                {
                    if (settings.Start)
                    {
                        SNAPTICDemoLog.Write("Starting LiveSplit timer.");
                        timer.Start();
                    }
                    else
                    {
                        SNAPTICDemoLog.Write("START signal ignored because Start setting is disabled.");
                    }
                }
                return;
            }

            if (settings.Reset && reset &&
                (state.CurrentPhase == TimerPhase.Running ||
                 state.CurrentPhase == TimerPhase.Paused ||
                 state.CurrentPhase == TimerPhase.Ended))
            {
                memory.SetEndSplitTrackingEnabled(true);
                endSplitCompleted = false;
                guardedAttemptCount = -1;
                guardedSplitIndex = -1;
                memory.ConsumeEndSplitSignal();
                SNAPTICDemoLog.Write("Resetting LiveSplit timer from main-menu reset signal.");
                timer.Reset();
                return;
            }

            if (endSplitCompleted)
            {
                // Contact is level-triggered while the player remains in the
                // bounds. Drain it after a confirmed split so it cannot queue
                // another segment during the same run.
                memory.ConsumeEndSplitSignal();
                return;
            }

            bool endSignal = memory.ConsumeEndSplitSignal();
            if (endSignal)
            {
                SNAPTICDemoLog.Write(
                    "Memory reader produced END SPLIT signal. " +
                    "Split=" + settings.Split +
                    ", EndSplit=" + settings.EndSplit +
                    ", TimerPhase=" + state.CurrentPhase +
                    ", CurrentSplitIndex=" + state.CurrentSplitIndex + ".");
            }

            if (settings.Split && settings.EndSplit &&
                state.CurrentPhase == TimerPhase.Running && endSignal)
            {
                int previousSplitIndex = state.CurrentSplitIndex;
                TimerPhase previousPhase = state.CurrentPhase;
                SNAPTICDemoLog.Write("Calling TimerModel.Split() for End Split.");
                timer.Split();

                // TimerModel.Split is synchronous. Only arm the guard after
                // LiveSplit actually advances or finishes.
                endSplitCompleted = state.CurrentSplitIndex != previousSplitIndex ||
                    state.CurrentPhase != previousPhase;
                if (endSplitCompleted)
                {
                    memory.SetEndSplitTrackingEnabled(false);
                    guardedAttemptCount = state.Run?.AttemptCount ?? -1;
                    guardedSplitIndex = state.CurrentSplitIndex;
                    SNAPTICDemoLog.Write(
                        "End Split confirmed by LiveSplit. New TimerPhase=" + state.CurrentPhase +
                        ", CurrentSplitIndex=" + state.CurrentSplitIndex +
                        ", AttemptCount=" + guardedAttemptCount + ".");
                }
                else
                {
                    SNAPTICDemoLog.Write(
                        "WARNING: TimerModel.Split() returned without LiveSplit advancing. " +
                        "TimerPhase=" + state.CurrentPhase +
                        ", CurrentSplitIndex=" + state.CurrentSplitIndex + ". Waiting for another End Split entry/crossing event.");
                }
            }
        }

        public override void Dispose()
        {
            SNAPTICDemoLog.Write("Component disposed.");
            memory.Dispose();
            settings.Dispose();
        }
    }
}
