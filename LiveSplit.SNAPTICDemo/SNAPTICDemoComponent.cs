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
            timer = new TimerModel { CurrentState = state };
            memory = new GodotMemoryReader();
            settings = new SNAPTICDemoSettings();
        }

        public override Control GetSettingsControl(LayoutMode mode) => settings;
        public override XmlNode GetSettings(XmlDocument document) => settings.GetSettings(document);
        public override void SetSettings(XmlNode node) => settings.SetSettings(node);

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

            int attemptCount = state.Run?.AttemptCount ?? -1;
            if (endSplitCompleted &&
                (attemptCount != guardedAttemptCount ||
                 state.CurrentSplitIndex != guardedSplitIndex))
            {
                endSplitCompleted = false;
            }

            if (state.CurrentPhase == TimerPhase.NotRunning)
            {
                endSplitCompleted = false;
                guardedAttemptCount = -1;
                guardedSplitIndex = -1;
                memory.ConsumeEndSplitSignal();
                if (settings.Start && start)
                    timer.Start();
                return;
            }

            if (settings.Reset && reset &&
                (state.CurrentPhase == TimerPhase.Running ||
                 state.CurrentPhase == TimerPhase.Paused ||
                 state.CurrentPhase == TimerPhase.Ended))
            {
                endSplitCompleted = false;
                guardedAttemptCount = -1;
                guardedSplitIndex = -1;
                memory.ConsumeEndSplitSignal();
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

            if (settings.Split && settings.EndSplit &&
                state.CurrentPhase == TimerPhase.Running &&
                memory.ConsumeEndSplitSignal())
            {
                int previousSplitIndex = state.CurrentSplitIndex;
                TimerPhase previousPhase = state.CurrentPhase;
                timer.Split();

                // TimerModel.Split is synchronous. Only arm the guard after
                // LiveSplit actually advances or finishes; otherwise the
                // latched contact remains eligible to retry on the next update.
                endSplitCompleted = state.CurrentSplitIndex != previousSplitIndex ||
                    state.CurrentPhase != previousPhase;
                if (endSplitCompleted)
                {
                    guardedAttemptCount = state.Run?.AttemptCount ?? -1;
                    guardedSplitIndex = state.CurrentSplitIndex;
                }
            }
        }

        public override void Dispose()
        {
            memory.Dispose();
            settings.Dispose();
        }
    }
}
