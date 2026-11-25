using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ProjectZ.InGame.Scripting
{
    /// <summary>
    /// Per-frame interpreter for the normalized Oracle script stream. It keeps
    /// the native counter1/counter2 ordering: wait counters run before command
    /// dispatch, movement applies its speed only while counter2 is non-zero,
    /// and a command explicitly says whether it continues, yields or blocks.
    /// </summary>
    internal sealed class CutsceneCommandRunner
    {
        private enum Result
        {
            Continue,
            Yield,
            Block,
            End,
        }

        private readonly CutsceneScriptTable _table;
        private readonly ICutsceneCommandHost _host;
        private readonly Stack<int> _returns = new();

        private int _instruction;
        private int _counter1;
        private int _counter2;
        private int _nextInstruction = -1;
        private bool _waitingForText;

        public bool Active { get; private set; }
        public string Script { get; private set; } = "";
        public int Instruction => _instruction;
        public int Counter1 => _counter1;
        public int Counter2 => _counter2;

        public CutsceneCommandRunner(CutsceneScriptTable table, ICutsceneCommandHost host)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public void Start(string script)
        {
            Start(_table.StartIndex(script));
        }

        public void Start(int firstInstruction)
        {
            if (firstInstruction < 0 || firstInstruction >= _table.Commands.Count)
                throw new ArgumentOutOfRangeException(nameof(firstInstruction));

            _instruction = firstInstruction;
            Script = _table.Commands[firstInstruction].Script;
            _counter1 = 0;
            _counter2 = 0;
            _nextInstruction = -1;
            _waitingForText = false;
            _returns.Clear();
            Active = true;
        }

        public void Update()
        {
            if (!Active || _host.ScriptExecutionBlocked)
                return;

            if (_waitingForText)
            {
                if (_host.TextVisible)
                    return;
                _waitingForText = false;
            }

            // interactionRunScript decrements counter1 first, then counter2.
            if (_counter1 != 0)
            {
                if (--_counter1 != 0)
                    return;
            }
            else if (_counter2 != 0)
            {
                if (--_counter2 != 0)
                    _host.ApplySpeed();
                return;
            }

            var dispatches = 0;
            while (Active)
            {
                if (_instruction < 0 || _instruction >= _table.Commands.Count)
                    throw new InvalidOperationException(
                        $"Script '{Script}' reached EOF without scriptend.");

                if (++dispatches > 512)
                    throw new InvalidOperationException(
                        $"Script '{Script}' did not yield near instruction {_instruction}.");

                var command = _table.Commands[_instruction];
                _nextInstruction = -1;
                var result = Execute(command);

                if (result == Result.Block)
                {
                    if (_nextInstruction >= 0)
                        _instruction = _nextInstruction;
                    return;
                }

                _instruction = _nextInstruction >= 0 ? _nextInstruction : _instruction + 1;

                if (result == Result.End || result == Result.Yield)
                    return;
            }
        }

        private Result Execute(CutsceneCommand command)
        {
            switch (command.Opcode)
            {
                case CutsceneOpcode.ScriptJump:
                    _nextInstruction = command.Target;
                    // Local scriptjump relocates a copied-buffer target and
                    // resumes on the next update; always yields here so a
                    // backward loop cannot spin inside one frame.
                    return Result.Yield;

                case CutsceneOpcode.CallScript:
                    _returns.Push(_instruction + 1);
                    _nextInstruction = command.Target;
                    return Result.Yield;

                case CutsceneOpcode.ReturnScript:
                    if (_returns.Count == 0)
                        throw new InvalidOperationException(
                            $"Script '{Script}' returned without a call at {command.Index}.");
                    _nextInstruction = _returns.Pop();
                    return Result.Yield;

                case CutsceneOpcode.ScriptEnd:
                    Active = false;
                    _host.ScriptEnded();
                    return Result.End;

                case CutsceneOpcode.Wait:
                    _counter1 = command.Arg0 & 0xff;
                    if (_counter1 == 0)
                        return Result.Continue;
                    _nextInstruction = _instruction + 1;
                    return Result.Block;

                case CutsceneOpcode.DisableInput:
                    _host.SetDisabledObjects(0x81);
                    return Result.Continue;

                case CutsceneOpcode.EnableInput:
                    _host.SetDisabledObjects(0);
                    return Result.Continue;

                case CutsceneOpcode.SetDisabledObjectsTo11:
                    _host.SetDisabledObjects(0x11);
                    return Result.Continue;

                case CutsceneOpcode.ShowText:
                    if (!_host.ShowText(command.Arg0, command.Payload))
                        return Result.Continue;
                    _waitingForText = true;
                    _nextInstruction = _instruction + 1;
                    return Result.Block;

                case CutsceneOpcode.SetAnimation:
                    _host.SetAnimation(command.Arg0);
                    return Result.Yield;

                case CutsceneOpcode.SetSpeed:
                    _host.SetSpeed(command.Arg0);
                    return Result.Yield;

                case CutsceneOpcode.SetAngle:
                    _host.SetAngle(command.Arg0);
                    return Result.Yield;

                case CutsceneOpcode.ApplySpeed:
                    _counter2 = command.Arg0 & 0xff;
                    if (_counter2 == 0)
                        return Result.Continue;
                    _nextInstruction = _instruction + 1;
                    return Result.Block;

                case CutsceneOpcode.MoveUp:
                case CutsceneOpcode.MoveRight:
                case CutsceneOpcode.MoveDown:
                case CutsceneOpcode.MoveLeft:
                    _host.SetAngle(command.Opcode switch
                    {
                        CutsceneOpcode.MoveUp => 0x00,
                        CutsceneOpcode.MoveRight => 0x08,
                        CutsceneOpcode.MoveDown => 0x10,
                        _ => 0x18,
                    });
                    _host.SetMovementAnimation(command.Arg0);
                    _counter2 = command.Arg0 & 0xff;
                    if (_counter2 == 0)
                        return Result.Continue;
                    _nextInstruction = _instruction + 1;
                    return Result.Block;

                case CutsceneOpcode.WriteObjectByte:
                    _host.WriteObjectByte(command.Arg0, command.Arg1);
                    return Result.Yield;

                case CutsceneOpcode.PlaySound:
                    _host.PlaySound(command.Payload);
                    return Result.Yield;

                case CutsceneOpcode.Asm15:
                    _host.RunAsm15(command.Payload);
                    return Result.Continue;

                case CutsceneOpcode.JumpIfGlobalFlagSet:
                    if (_host.GetGlobalFlag(command.Arg0))
                        _nextInstruction = command.Target;
                    return Result.Continue;

                case CutsceneOpcode.JumpIfMemorySet:
                    if ((_host.ReadMemory(command.Arg0) & command.Arg1) != 0)
                        _nextInstruction = command.Target;
                    return Result.Continue;

                case CutsceneOpcode.SetGlobalFlag:
                    _host.SetGlobalFlag(command.Arg0);
                    return Result.Continue;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported opcode {command.Opcode} at {command.Index}.");
            }
        }
    }

    /// <summary>
    /// Runtime services a script needs from the room/actor owner. Commands
    /// deliberately operate on the runner's bound actor, not on a global id.
    /// </summary>
    internal interface ICutsceneCommandHost
    {
        long FrameCounter { get; }
        bool TextVisible { get; }
        bool ScriptExecutionBlocked { get; }

        bool ShowText(int textId, string sourceName);
        void SetAnimation(int animation);
        void SetMovementAnimation(int frames);
        void SetSpeed(int speed);
        void SetAngle(int angle);
        void ApplySpeed();
        void WriteObjectByte(int address, int value);
        void SetActorVisible(bool visible);
        void SetActorZ(int zFixed);

        int ReadMemory(int address);
        void WriteMemory(int address, int value);
        bool GetGlobalFlag(int flag);
        void SetGlobalFlag(int flag);
        void SetDisabledObjects(int value);

        void PlaySound(string name);
        void RunAsm15(string helper);
        void ScriptEnded();
    }
}
