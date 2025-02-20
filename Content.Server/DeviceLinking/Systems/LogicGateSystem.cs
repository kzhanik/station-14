using Content.Server.DeviceLinking.Components;
using Content.Server.DeviceNetwork;
using Content.Server.MachineLinking.Events;
using Content.Shared.DeviceLinking;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Tools;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Utility;
using SignalReceivedEvent = Content.Server.DeviceLinking.Events.SignalReceivedEvent;

namespace Content.Server.DeviceLinking.Systems;

public sealed class LogicGateSystem : EntitySystem
{
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;

    private readonly int GateCount = Enum.GetValues(typeof(LogicGate)).Length;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LogicGateComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<LogicGateComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<LogicGateComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<LogicGateComponent, SignalReceivedEvent>(OnSignalReceived);
    }

    private void OnInit(EntityUid uid, LogicGateComponent comp, ComponentInit args)
    {
        _deviceLink.EnsureSinkPorts(uid, comp.InputPortA, comp.InputPortB);
        _deviceLink.EnsureSourcePorts(uid, comp.OutputPort);
    }

    private void OnExamined(EntityUid uid, LogicGateComponent comp, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("logic-gate-examine", ("gate", comp.Gate.ToString().ToUpper())));
    }

    private void OnInteractUsing(EntityUid uid, LogicGateComponent comp, InteractUsingEvent args)
    {
        if (args.Handled || !_tool.HasQuality(args.Used, comp.CycleQuality))
            return;

        // cycle through possible gates
        var gate = (int) comp.Gate;
        gate = ++gate % GateCount;
        comp.Gate = (LogicGate) gate;

        // since gate changed the output probably has too, update it
        UpdateOutput(uid, comp);

        // notify the user
        _audio.PlayPvs(comp.CycleSound, uid);
        var msg = Loc.GetString("logic-gate-cycle", ("gate", comp.Gate.ToString().ToUpper()));
        _popup.PopupEntity(msg, uid, args.User);
        _appearance.SetData(uid, LogicGateVisuals.Gate, comp.Gate);
    }

    private void OnSignalReceived(EntityUid uid, LogicGateComponent comp, ref SignalReceivedEvent args)
    {
        // default to momentary for compatibility with non-logic signals.
        // currently only door status and logic gates have logic signal state.
        var state = SignalState.Momentary;
        args.Data?.TryGetValue(DeviceNetworkConstants.LogicState, out state);

        // update the state for the correct port
        if (args.Port == comp.InputPortA)
        {
            comp.StateA = state;
        }
        else if (args.Port == comp.InputPortB)
        {
            comp.StateB = state;
        }

        UpdateOutput(uid, comp);
    }

    /// <summary>
    /// Handle the logic for a logic gate, invoking the port if the output changed.
    /// </summary>
    private void UpdateOutput(EntityUid uid, LogicGateComponent comp)
    {
        // get the new output value now that it's changed
        var a = comp.StateA == SignalState.High;
        var b = comp.StateB == SignalState.High;
        var output = false;
        switch (comp.Gate)
        {
            case LogicGate.Or:
                output = a || b;
                break;
            case LogicGate.And:
                output = a && b;
                break;
            case LogicGate.Xor:
                output = a != b;
                break;
            case LogicGate.Nor:
                output = !(a || b);
                break;
            case LogicGate.Nand:
                output = !(a && b);
                break;
            case LogicGate.Xnor:
                output = a == b;
                break;
        }

        // only send a payload if it actually changed
        if (output != comp.LastOutput)
        {
            comp.LastOutput = output;

            var data = new NetworkPayload
            {
                [DeviceNetworkConstants.LogicState] = output ? SignalState.High : SignalState.Low
            };

            _deviceLink.InvokePort(uid, comp.OutputPort, data);
        }
    }
}
