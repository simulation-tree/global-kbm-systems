using InputDevices.Components;
using InputDevices.Messages;
using SharpHook;
using SharpHook.Data;
using Simulation;
using System;
using System.Diagnostics;
using System.Numerics;
using Worlds;

namespace InputDevices.Systems
{
    public partial class GlobalKeyboardAndMouseSystem : SystemBase, IListener<InputUpdate>
    {
        private static bool globalMouseMoved;
        private static bool globalMouseScrolled;
        private static KeyboardState globalCurrentKeyboard = default;
        private static KeyboardState globalLastKeyboard = default;
        private static MouseState globalCurrentMouse = default;
        private static MouseState globalLastMouse = default;
        private static Vector2 globalMousePosition;
        private static Vector2 globalMouseScroll;

        private readonly World world;
        private readonly int keyboardType;
        private readonly int mouseType;
        private readonly int globalTagType;
        private readonly int timestampType;
        private double time;

        private TaskPoolGlobalHook? kbmHook;
        private uint globalKeyboardEntity;
        private uint globalMouseEntity;

        public GlobalKeyboardAndMouseSystem(Simulator simulator, World world) : base(simulator)
        {
            this.world = world;
            Schema schema = world.Schema;
            keyboardType = schema.GetComponentType<IsKeyboard>();
            mouseType = schema.GetComponentType<IsMouse>();
            globalTagType = schema.GetTagType<IsGlobal>();
            timestampType = schema.GetComponentType<LastDeviceUpdateTime>();
        }

        public override void Dispose()
        {
            if (kbmHook is not null)
            {
                kbmHook.Dispose();
            }
        }

        void IListener<InputUpdate>.Receive(ref InputUpdate message)
        {
            time += message.deltaTime;
            FindGlobalDevices();
            UpdateStates();
        }

        private void FindGlobalDevices()
        {
            globalKeyboardEntity = default;
            globalMouseEntity = default;
            foreach (Chunk chunk in world.Chunks)
            {
                Definition definition = chunk.Definition;
                if (definition.ContainsTag(globalTagType))
                {
                    if (definition.ContainsComponent(keyboardType))
                    {
                        if (globalKeyboardEntity != default)
                        {
                            throw new InvalidOperationException("Multiple global keyboard entities found");
                        }

                        globalKeyboardEntity = chunk.Entities[0];
                    }

                    if (definition.ContainsComponent(mouseType))
                    {
                        if (globalMouseEntity != default)
                        {
                            throw new InvalidOperationException("Multiple global mice entities found");
                        }

                        globalMouseEntity = chunk.Entities[0];
                    }
                }
            }

            if (kbmHook is null)
            {
                if (globalKeyboardEntity != default || globalMouseEntity != default)
                {
                    kbmHook = InitializeGlobalHook();
                    Trace.WriteLine("Global keyboard and mouse hook initialized");
                }
            }
        }

        private TaskPoolGlobalHook InitializeGlobalHook()
        {
            TaskPoolGlobalHook kbmHook = new();
            kbmHook.KeyPressed += OnKeyPressed;
            kbmHook.KeyReleased += OnKeyReleased;
            kbmHook.MousePressed += OnMousePressed;
            kbmHook.MouseReleased += OnMouseReleased;
            kbmHook.MouseDragged += OnMouseDragged;
            kbmHook.MouseMoved += OnMouseMoved;
            kbmHook.MouseWheel += OnMouseWheel;
            kbmHook.RunAsync();
            return kbmHook;
        }

        private void UpdateStates()
        {
            if (globalKeyboardEntity != default)
            {
                ref IsKeyboard component = ref world.GetComponent<IsKeyboard>(globalKeyboardEntity, keyboardType);
                bool keyboardUpdated = false;
                for (int i = 0; i < KeyboardState.MaxKeyCount; i++)
                {
                    bool next = globalCurrentKeyboard[i];
                    bool previous = globalLastKeyboard[i];
                    ButtonState state = new(component.currentState[i], component.lastState[i]);
                    ButtonState current = new(previous, next);
                    if (state != current)
                    {
                        keyboardUpdated = true;
                        globalLastKeyboard[i] = next;
                        if (current.value == ButtonState.State.Held)
                        {
                            component.currentState[i] = true;
                            component.lastState[i] = true;
                        }
                        else if (current.value == ButtonState.State.WasPressed)
                        {
                            component.currentState[i] = true;
                            component.lastState[i] = false;
                        }
                        else if (current.value == ButtonState.State.WasReleased)
                        {
                            component.currentState[i] = false;
                            component.lastState[i] = true;
                        }
                        else
                        {
                            component.currentState[i] = false;
                            component.lastState[i] = false;
                        }
                    }
                }

                if (keyboardUpdated)
                {
                    ref LastDeviceUpdateTime timestamp = ref world.GetComponent<LastDeviceUpdateTime>(globalKeyboardEntity, timestampType);
                    timestamp.time = time;
                }
            }

            if (globalMouseEntity != default)
            {
                ref IsMouse component = ref world.GetComponent<IsMouse>(globalMouseEntity, mouseType);
                bool mouseUpdated = false;
                for (uint i = 0; i < MouseState.MaxButtonCount; i++)
                {
                    bool next = globalCurrentMouse[i];
                    bool previous = globalLastMouse[i];
                    ButtonState state = new(component.currentState[i], component.lastState[i]);
                    ButtonState current = new(previous, next);
                    if (state != current)
                    {
                        mouseUpdated = true;
                        globalLastMouse[i] = next;
                        if (current.value == ButtonState.State.Held)
                        {
                            component.currentState[i] = true;
                            component.lastState[i] = true;
                        }
                        else if (current.value == ButtonState.State.WasPressed)
                        {
                            component.currentState[i] = true;
                            component.lastState[i] = false;
                        }
                        else if (current.value == ButtonState.State.WasReleased)
                        {
                            component.currentState[i] = false;
                            component.lastState[i] = true;
                        }
                        else
                        {
                            component.currentState[i] = false;
                            component.lastState[i] = false;
                        }
                    }
                }

                if (globalMouseMoved)
                {
                    ref Vector2 currentPosition = ref component.currentState.position;
                    ref Vector2 currentDelta = ref component.currentState.delta;
                    component.lastState.position = currentPosition;
                    Vector2 delta = globalMousePosition - currentPosition;
                    currentPosition = globalMousePosition;
                    currentDelta = delta;
                    mouseUpdated = true;
                }

                if (globalMouseScrolled)
                {
                    ref Vector2 currentScroll = ref component.currentState.scroll;
                    component.lastState.scroll = currentScroll;
                    currentScroll = globalMouseScroll;
                    mouseUpdated = true;
                }

                if (mouseUpdated)
                {
                    ref LastDeviceUpdateTime timestamp = ref world.GetComponent<LastDeviceUpdateTime>(globalMouseEntity, timestampType);
                    timestamp.time = time;
                }

                globalMouseMoved = false;
                globalMouseScrolled = false;
            }
        }

        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode != KeyCode.VcUndefined)
            {
                Keyboard.Button control = GetControl(e.Data.KeyCode);
                if (!globalCurrentKeyboard[(int)control])
                {
                    globalCurrentKeyboard[(int)control] = true;
                }
            }
        }

        private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode != KeyCode.VcUndefined)
            {
                Keyboard.Button control = GetControl(e.Data.KeyCode);
                if (globalCurrentKeyboard[(int)control])
                {
                    globalCurrentKeyboard[(int)control] = false;
                }
            }
        }

        private void OnMousePressed(object? sender, MouseHookEventArgs e)
        {
            uint control = (uint)e.Data.Button;
            if (!globalCurrentMouse[control])
            {
                globalCurrentMouse[control] = true;
            }
        }

        private void OnMouseReleased(object? sender, MouseHookEventArgs e)
        {
            uint control = (uint)e.Data.Button;
            if (globalCurrentMouse[control])
            {
                globalCurrentMouse[control] = false;
            }
        }

        private void OnMouseDragged(object? sender, MouseHookEventArgs e)
        {
            globalMousePosition = new Vector2(e.Data.X, e.Data.Y);
            globalMouseMoved = true;
        }

        private void OnMouseMoved(object? sender, MouseHookEventArgs e)
        {
            globalMousePosition = new Vector2(e.Data.X, e.Data.Y);
            globalMouseMoved = true;
        }

        private void OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
        {
            globalMouseScroll = new Vector2(e.Data.X, e.Data.Y);
            globalMouseScrolled = true;
        }

        private static Keyboard.Button GetControl(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.Vc0 => Keyboard.Button.Digit0,
                KeyCode.Vc1 => Keyboard.Button.Digit1,
                KeyCode.Vc2 => Keyboard.Button.Digit2,
                KeyCode.Vc3 => Keyboard.Button.Digit3,
                KeyCode.Vc4 => Keyboard.Button.Digit4,
                KeyCode.Vc5 => Keyboard.Button.Digit5,
                KeyCode.Vc6 => Keyboard.Button.Digit6,
                KeyCode.Vc7 => Keyboard.Button.Digit7,
                KeyCode.Vc8 => Keyboard.Button.Digit8,
                KeyCode.Vc9 => Keyboard.Button.Digit9,
                KeyCode.VcA => Keyboard.Button.A,
                KeyCode.VcB => Keyboard.Button.B,
                KeyCode.VcC => Keyboard.Button.C,
                KeyCode.VcD => Keyboard.Button.D,
                KeyCode.VcE => Keyboard.Button.E,
                KeyCode.VcF => Keyboard.Button.F,
                KeyCode.VcG => Keyboard.Button.G,
                KeyCode.VcH => Keyboard.Button.H,
                KeyCode.VcI => Keyboard.Button.I,
                KeyCode.VcJ => Keyboard.Button.J,
                KeyCode.VcK => Keyboard.Button.K,
                KeyCode.VcL => Keyboard.Button.L,
                KeyCode.VcM => Keyboard.Button.M,
                KeyCode.VcN => Keyboard.Button.N,
                KeyCode.VcO => Keyboard.Button.O,
                KeyCode.VcP => Keyboard.Button.P,
                KeyCode.VcQ => Keyboard.Button.Q,
                KeyCode.VcR => Keyboard.Button.R,
                KeyCode.VcS => Keyboard.Button.S,
                KeyCode.VcT => Keyboard.Button.T,
                KeyCode.VcU => Keyboard.Button.U,
                KeyCode.VcV => Keyboard.Button.V,
                KeyCode.VcW => Keyboard.Button.W,
                KeyCode.VcX => Keyboard.Button.X,
                KeyCode.VcY => Keyboard.Button.Y,
                KeyCode.VcZ => Keyboard.Button.Z,
                KeyCode.VcF1 => Keyboard.Button.F1,
                KeyCode.VcF2 => Keyboard.Button.F2,
                KeyCode.VcF3 => Keyboard.Button.F3,
                KeyCode.VcF4 => Keyboard.Button.F4,
                KeyCode.VcF5 => Keyboard.Button.F5,
                KeyCode.VcF6 => Keyboard.Button.F6,
                KeyCode.VcF7 => Keyboard.Button.F7,
                KeyCode.VcF8 => Keyboard.Button.F8,
                KeyCode.VcF9 => Keyboard.Button.F9,
                KeyCode.VcF10 => Keyboard.Button.F10,
                KeyCode.VcF11 => Keyboard.Button.F11,
                KeyCode.VcF12 => Keyboard.Button.F12,
                KeyCode.VcF13 => Keyboard.Button.F13,
                KeyCode.VcF14 => Keyboard.Button.F14,
                KeyCode.VcF15 => Keyboard.Button.F15,
                KeyCode.VcF16 => Keyboard.Button.F16,
                KeyCode.VcF17 => Keyboard.Button.F17,
                KeyCode.VcF18 => Keyboard.Button.F18,
                KeyCode.VcF19 => Keyboard.Button.F19,
                KeyCode.VcF20 => Keyboard.Button.F20,
                KeyCode.VcF21 => Keyboard.Button.F21,
                KeyCode.VcF22 => Keyboard.Button.F22,
                KeyCode.VcF23 => Keyboard.Button.F23,
                KeyCode.VcF24 => Keyboard.Button.F24,
                KeyCode.VcNumLock => Keyboard.Button.NumLock,
                KeyCode.VcScrollLock => Keyboard.Button.ScrollLock,
                KeyCode.VcLeftShift => Keyboard.Button.LeftShift,
                KeyCode.VcRightShift => Keyboard.Button.RightShift,
                KeyCode.VcLeftControl => Keyboard.Button.LeftControl,
                KeyCode.VcRightControl => Keyboard.Button.RightControl,
                KeyCode.VcLeftAlt => Keyboard.Button.LeftAlt,
                KeyCode.VcRightAlt => Keyboard.Button.RightAlt,
                KeyCode.VcLeftMeta => Keyboard.Button.LeftGui,
                KeyCode.VcRightMeta => Keyboard.Button.RightGui,
                KeyCode.VcSpace => Keyboard.Button.Space,
                KeyCode.VcQuote => Keyboard.Button.Apostrophe,
                KeyCode.VcComma => Keyboard.Button.Comma,
                KeyCode.VcMinus => Keyboard.Button.Minus,
                KeyCode.VcPeriod => Keyboard.Button.Period,
                KeyCode.VcSlash => Keyboard.Button.Slash,
                KeyCode.VcSemicolon => Keyboard.Button.Semicolon,
                KeyCode.VcEquals => Keyboard.Button.Equals,
                KeyCode.VcOpenBracket => Keyboard.Button.LeftBracket,
                KeyCode.VcBackslash => Keyboard.Button.Backslash,
                KeyCode.VcCloseBracket => Keyboard.Button.RightBracket,
                KeyCode.VcBackQuote => Keyboard.Button.Grave,
                KeyCode.VcEscape => Keyboard.Button.Escape,
                KeyCode.VcEnter => Keyboard.Button.Enter,
                KeyCode.VcTab => Keyboard.Button.Tab,
                KeyCode.VcBackspace => Keyboard.Button.Backspace,
                KeyCode.VcInsert => Keyboard.Button.Insert,
                KeyCode.VcDelete => Keyboard.Button.Delete,
                KeyCode.VcRight => Keyboard.Button.Right,
                KeyCode.VcLeft => Keyboard.Button.Left,
                KeyCode.VcDown => Keyboard.Button.Down,
                KeyCode.VcUp => Keyboard.Button.Up,
                KeyCode.VcHome => Keyboard.Button.Home,
                KeyCode.VcEnd => Keyboard.Button.End,
                _ => throw new NotImplementedException($"Key code {keyCode} is not implemented")
            };
        }
    }
}