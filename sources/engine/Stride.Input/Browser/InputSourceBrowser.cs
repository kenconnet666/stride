// Copyright (c) .NET Foundation and Contributors.
// Distributed under the MIT license. See LICENSE.md in the project root.
#if STRIDE_PLATFORM_BROWSER
using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;

namespace Stride.Input;

/// <summary>Browser events supplied by the canvas host, consumed by the normal input update.</summary>
public sealed class InputSourceBrowser : InputSourceBase
{
    private BrowserKeyboard keyboard;
    private BrowserMouse mouse;
    private BrowserPointer pointer;
    private readonly Dictionary<int, BrowserGamePad> gamePads = [];
    private AccelerometerSensor accelerometer;
    private UserAccelerationSensor userAcceleration;
    private GravitySensor gravity;
    private bool disposed;

    public override void Initialize(InputManager inputManager)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (keyboard is not null) throw new InvalidOperationException("Browser input is already initialized.");
        keyboard = new BrowserKeyboard(this);
        mouse = new BrowserMouse(this);
        pointer = new BrowserPointer(this);
        RegisterDevice(keyboard);
        RegisterDevice(mouse);
        RegisterDevice(pointer);
    }

    /// <summary>Accepts a physical KeyboardEvent.code; false means the code is not mapped.</summary>
    public bool SubmitKey(string code, bool down)
    {
        CheckReady();
        var name = code switch
        {
            "ArrowLeft" => "Left", "ArrowRight" => "Right", "ArrowUp" => "Up", "ArrowDown" => "Down",
            "ShiftLeft" => "LeftShift", "ShiftRight" => "RightShift",
            "ControlLeft" => "LeftCtrl", "ControlRight" => "RightCtrl",
            "AltLeft" => "LeftAlt", "AltRight" => "RightAlt", "MetaLeft" => "LeftWin", "MetaRight" => "RightWin",
            "Backspace" => "Back", "Enter" => "Return", "NumpadEnter" => "Return",
            "Space" => "Space", "Escape" => "Escape", "Tab" => "Tab",
            "Home" => "Home", "End" => "End", "PageUp" => "PageUp", "PageDown" => "PageDown",
            "Insert" => "Insert", "Delete" => "Delete", "CapsLock" => "CapsLock",
            _ when code?.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) && code[3] is >= 'A' and <= 'Z' => code[3..],
            _ when code?.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal) && char.IsAsciiDigit(code[5]) => "D" + code[5],
            _ when code?.Length is 2 or 3 && code[0] == 'F' && int.TryParse(code[1..], out var function) && function is >= 1 and <= 24 => code,
            _ => null,
        };
        if (name is null || !Enum.TryParse<Keys>(name, out var key)) return false;
        if (down) keyboard.HandleKeyDown(key); else keyboard.HandleKeyUp(key);
        return true;
    }

    public void SetSurfaceSize(int width, int height)
    {
        CheckReady();
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        var size = new Vector2(Math.Max(1, width), Math.Max(1, height));
        mouse.Resize(size);
        pointer.Resize(size);
    }

    public void SubmitMouseMove(float x, float y) { CheckReady(); mouse.Move(Position(x, y)); }
    public void SubmitMouseButton(int button, bool down)
    {
        CheckReady();
        if (button is < 0 or > 4) return;
        mouse.Button((MouseButton)button, down);
    }
    /// <summary>Wheel motion in ticks, positive upwards.</summary>
    public void SubmitWheel(float ticks) { CheckReady(); if (float.IsFinite(ticks)) mouse.Wheel(ticks); }
    public void CancelMouse() { CheckReady(); mouse.Release(); }
    public void SubmitPointer(int id, PointerEventType type, float x, float y)
    {
        CheckReady();
        if (type is < PointerEventType.Pressed or > PointerEventType.Canceled) throw new ArgumentOutOfRangeException(nameof(type));
        pointer.Submit(id, type, Position(x, y));
    }

    /// <summary>Accepts the state of a gamepad, which the host polls each frame.</summary>
    /// <param name="index">The index the browser gave the gamepad.</param>
    /// <param name="buttons">One bit per pressed button, in the order of the "standard" layout of the Gamepad API.</param>
    /// <remarks>The axes are those of the Gamepad API: a thumbstick pushed up gives a negative Y.</remarks>
    public void SubmitGamePad(int index, string name, int buttons, float leftX, float leftY, float rightX, float rightY, float leftTrigger, float rightTrigger)
    {
        CheckReady();
        if (!gamePads.TryGetValue(index, out var gamePad))
        {
            gamePad = new BrowserGamePad(this, name, index);
            gamePads.Add(index, gamePad);
            RegisterDevice(gamePad);
        }
        gamePad.Submit(buttons, new Vector2(leftX, -leftY), new Vector2(rightX, -rightY), leftTrigger, rightTrigger);
    }

    /// <summary>Accepts a devicemotion event of the page: the acceleration of the device in m/s², with gravity and without it.</summary>
    /// <remarks>The axes are those of the device: x to the right of its screen, y to its top, z out of it. The acceleration
    /// without gravity is NaN when the browser does not know it.</remarks>
    public void SubmitMotion(float x, float y, float z, float userX, float userY, float userZ)
    {
        CheckReady();
        // Registered at the first event: a desktop browser has the event and never sends it
        if (accelerometer is null)
        {
            accelerometer = new AccelerometerSensor(this, "Browser");
            userAcceleration = new UserAccelerationSensor(this, "Browser");
            gravity = new GravitySensor(this, "Browser");
            RegisterDevice(accelerometer);
            RegisterDevice(userAcceleration);
            RegisterDevice(gravity);
        }
        var withGravity = DeviceToWorld(x, y, z);
        var user = float.IsFinite(userX) && float.IsFinite(userY) && float.IsFinite(userZ) ? DeviceToWorld(userX, userY, userZ) : Vector3.Zero;
        accelerometer.Acceleration = withGravity;
        userAcceleration.Acceleration = user;
        gravity.Vector = withGravity - user;
    }

    // The axes of Stride's sensors on Android, whose device axes are those of the browser
    private static Vector3 DeviceToWorld(float x, float y, float z) => new(-x, -z, y);

    /// <summary>Removes a gamepad that was disconnected.</summary>
    public void RemoveGamePad(int index)
    {
        CheckReady();
        if (gamePads.Remove(index, out var gamePad)) UnregisterDevice(gamePad);
    }

    /// <summary>Whether a control asked for text, so the host keeps a DOM text field focused.</summary>
    public bool IsTextInputActive { get; private set; }

    /// <summary>Raised when <see cref="IsTextInputActive"/> changes.</summary>
    /// <remarks>
    /// A canvas gets key codes only. Text, an input method's composition and the on-screen keyboard need a focused
    /// DOM text field, which the host owns: it forwards what is typed there with <see cref="SubmitText"/> and
    /// <see cref="SubmitComposition"/>.
    /// </remarks>
    public event Action<bool> TextInputActiveChanged;

    /// <summary>Accepts text that the DOM text field committed.</summary>
    public void SubmitText(string text)
    {
        CheckReady();
        if (!string.IsNullOrEmpty(text)) keyboard.Text(text, TextInputEventType.Input, 0, 0);
    }

    /// <summary>Accepts the text an input method is composing; an empty text ends the composition.</summary>
    public void SubmitComposition(string text, int selectionStart, int selectionLength)
    {
        CheckReady();
        text ??= string.Empty;
        selectionStart = Math.Clamp(selectionStart, 0, text.Length);
        keyboard.Text(text, TextInputEventType.Composition, selectionStart, Math.Clamp(selectionLength, 0, text.Length - selectionStart));
    }

    private void SetTextInputActive(bool active)
    {
        if (IsTextInputActive == active) return;
        IsTextInputActive = active;
        TextInputActiveChanged?.Invoke(active);
    }

    public override void Pause()
    {
        if (keyboard is null || disposed) return;
        foreach (var key in keyboard.DownKeys.ToArray()) keyboard.HandleKeyUp(key);
        mouse.Release();
        pointer.Cancel();
        foreach (var gamePad in gamePads.Values) gamePad.Submit(0, default, default, 0, 0);
    }
    public override void Dispose()
    {
        if (disposed) return;
        Pause();
        disposed = true;
        base.Dispose();
    }
    private void CheckReady()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (keyboard is null) throw new InvalidOperationException("Initialize browser input before submitting events.");
    }
    private static Vector2 Position(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        return new Vector2(x, y); // Captured pointers may move outside [0,1].
    }

    private sealed class BrowserKeyboard(InputSourceBrowser source) : KeyboardDeviceBase, ITextInputDevice
    {
        private readonly List<TextInputEvent> textEvents = [];

        public override string Name => "Browser keyboard";
        public override Guid Id { get; } = Guid.NewGuid();
        public override IInputSource Source => source;

        public override void Update(List<InputEvent> inputEvents)
        {
            base.Update(inputEvents);
            inputEvents.AddRange(textEvents);
            textEvents.Clear();
        }

        public void EnabledTextInput() => source.SetTextInputActive(true);
        public void DisableTextInput() => source.SetTextInputActive(false);

        public void Text(string text, TextInputEventType type, int compositionStart, int compositionLength)
        {
            var textEvent = InputEventPool<TextInputEvent>.GetOrCreate(this);
            textEvent.Text = text;
            textEvent.Type = type;
            textEvent.CompositionStart = compositionStart;
            textEvent.CompositionLength = compositionLength;
            textEvents.Add(textEvent);
        }
    }
    private sealed class BrowserMouse : MouseDeviceBase
    {
        public BrowserMouse(IInputSource source) { Source = source; SetSurfaceSize(Vector2.One); }
        public override string Name => "Browser mouse";
        public override Guid Id { get; } = Guid.NewGuid();
        public override IInputSource Source { get; }
        public override bool IsPositionLocked => false;
        public void Resize(Vector2 size) => SetSurfaceSize(size);
        public void Move(Vector2 normalized) => MouseState.HandleMove(normalized * SurfaceSize);
        public void Button(MouseButton button, bool down) { if (down) MouseState.HandleButtonDown(button); else MouseState.HandleButtonUp(button); }
        public void Wheel(float ticks) => MouseState.HandleMouseWheel(ticks);
        public void Release() { foreach (var button in DownButtons.ToArray()) MouseState.HandleButtonUp(button); }
        public override void SetPosition(Vector2 position) => throw new PlatformNotSupportedException("Browsers do not permit cursor warping.");
        public override void LockPosition(bool forceCenter = false) => throw new PlatformNotSupportedException("Pointer lock requires an asynchronous host user gesture.");
        public override void UnlockPosition() { }
    }
    private sealed class BrowserGamePad : GamePadDeviceBase
    {
        // The buttons of the "standard" layout, in its order. The triggers (6 and 7) are axes here.
        private static readonly GamePadButton[] StandardButtons =
        [
            GamePadButton.A, GamePadButton.B, GamePadButton.X, GamePadButton.Y, GamePadButton.LeftShoulder, GamePadButton.RightShoulder,
            GamePadButton.None, GamePadButton.None, GamePadButton.Back, GamePadButton.Start, GamePadButton.LeftThumb, GamePadButton.RightThumb,
            GamePadButton.PadUp, GamePadButton.PadDown, GamePadButton.PadLeft, GamePadButton.PadRight,
        ];
        private GamePadState state;
        private GamePadState submitted;

        public BrowserGamePad(IInputSource source, string name, int index)
        {
            Source = source;
            Name = name;
            SetIndexInternal(index, false);
        }

        public override string Name { get; }
        public override Guid Id { get; } = Guid.NewGuid();
        public override Guid ProductId { get; } = new("6d1d1bd5-bb9a-4d3b-8a6b-6f2c7a1e3b50");
        public override IInputSource Source { get; }
        protected override GamePadState RawState => state;

        public void Submit(int buttons, Vector2 leftThumb, Vector2 rightThumb, float leftTrigger, float rightTrigger)
        {
            submitted = new GamePadState { LeftThumb = leftThumb, RightThumb = rightThumb, LeftTrigger = leftTrigger, RightTrigger = rightTrigger };
            for (var i = 0; i < StandardButtons.Length; i++)
                if ((buttons & (1 << i)) != 0) submitted.Buttons |= StandardButtons[i];
        }

        public override void Update(List<InputEvent> inputEvents)
        {
            ClearButtonStates();
            foreach (var button in StandardButtons)
            {
                var down = (submitted.Buttons & button) != 0;
                if (button == GamePadButton.None || down == ((state.Buttons & button) != 0)) continue;
                var buttonEvent = InputEventPool<GamePadButtonEvent>.GetOrCreate(this);
                buttonEvent.Button = button;
                buttonEvent.IsDown = down;
                inputEvents.Add(buttonEvent);
                if (state.Update(buttonEvent)) UpdateButtonState(buttonEvent);
            }
            Axis(GamePadAxis.LeftThumbX, state.LeftThumb.X, submitted.LeftThumb.X);
            Axis(GamePadAxis.LeftThumbY, state.LeftThumb.Y, submitted.LeftThumb.Y);
            Axis(GamePadAxis.RightThumbX, state.RightThumb.X, submitted.RightThumb.X);
            Axis(GamePadAxis.RightThumbY, state.RightThumb.Y, submitted.RightThumb.Y);
            Axis(GamePadAxis.LeftTrigger, state.LeftTrigger, submitted.LeftTrigger);
            Axis(GamePadAxis.RightTrigger, state.RightTrigger, submitted.RightTrigger);

            void Axis(GamePadAxis axis, float current, float value)
            {
                if (current == value) return;
                var axisEvent = InputEventPool<GamePadAxisEvent>.GetOrCreate(this);
                axisEvent.Axis = axis;
                axisEvent.Value = value;
                inputEvents.Add(axisEvent);
                state.Update(axisEvent);
            }
        }

        // The vibration of the Gamepad API is asynchronous and belongs to the page
        public override void SetVibration(float smallLeft, float smallRight, float largeLeft, float largeRight) { }
    }
    private sealed class BrowserPointer : PointerDeviceBase
    {
        private readonly Dictionary<int, (int Slot, Vector2 Position)> active = [];
        public BrowserPointer(IInputSource source) { Source = source; SetSurfaceSize(Vector2.One); }
        public override string Name => "Browser touch and pen";
        public override Guid Id { get; } = Guid.NewGuid();
        public override IInputSource Source { get; }
        public void Resize(Vector2 size) => SetSurfaceSize(size);
        public void Submit(int id, PointerEventType type, Vector2 position)
        {
            if (type == PointerEventType.Pressed)
            {
                if (active.ContainsKey(id)) return;
                var slot = 0;
                while (active.Values.Any(point => point.Slot == slot)) slot++;
                active.Add(id, (slot, position));
            }
            if (!active.TryGetValue(id, out var point)) return;
            PointerState.PointerInputEvents.Add(new PointerDeviceState.InputEvent { Id = point.Slot, Type = type, Position = position });
            if (type is PointerEventType.Released or PointerEventType.Canceled) active.Remove(id);
            else active[id] = (point.Slot, position);
        }
        public void Cancel()
        {
            foreach (var (id, point) in active.ToArray()) Submit(id, PointerEventType.Canceled, point.Position);
        }
    }
}
#endif
