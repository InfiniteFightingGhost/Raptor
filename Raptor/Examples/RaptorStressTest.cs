using System;
using System.Collections.Generic;
using System.Diagnostics;
using Raptor;
using Raptor.Attributes;
using UnityEngine;

[RaptorModule("enemy")]
public unsafe class RaptorStressTest : MonoBehaviour
{
    public enum ExecutionMode
    {
        RaptorVM_FFI,
        RaptorVM_PropertyMapped,
        NativeCSharp,
    }

    [Header("Benchmark Settings")]
    [Tooltip("Number of instances to spawn.")]
    public int spawnCount = 50000;

    [Tooltip("Toggle between VM and Native C# execution.")]
    public ExecutionMode mode = ExecutionMode.RaptorVM_PropertyMapped;

    [Tooltip("Disable mesh renderers to isolate CPU VM speed from GPU rendering.")]
    public bool renderMeshes = true;

    [Header("Visuals")]
    public Material cubeMaterial;
    public Mesh cubeMesh; // Required for DrawMeshInstanced. Assign a default Cube mesh here.

    private struct Agent
    {
        public double X;
        public double Z;
        public double SpeedOffset;
    }

    // Flat memory arrays for L1 Cache perfection
    private Agent[] _agentsArray;
    private Matrix4x4[][] _matrixBatches;
    private ScriptEngine _engine;
    private VMChunk _ffiHeavyChunk;
    private VMChunk _propertyMappedChunk;
    private FFIHostTable _ffiTable;
    private VirtualMachine _sharedVM;

    // Shared FFI communication state
    private float _currentTime;
    private double _tempX;
    private double _tempZ;
    private double _tempOffset;
    private double _newX;
    private double _newZ;

    private void Start()
    {
        _engine = new ScriptEngine();
        _ffiTable = new FFIHostTable();
        _ffiTable.RegisterModule(this);

        _ffiTable.Register(
            "math.cos",
            100,
            (ref VMState s) =>
            {
                double val = s.RegPtr[0];
                s.RegPtr[0] = MathF.Cos((float)val);
            }
        );
        _ffiTable.Register(
            "math.sin",
            101,
            (ref VMState s) =>
            {
                double val = s.RegPtr[0];
                s.RegPtr[0] = MathF.Sin((float)val);
            }
        );

        _engine.RegisterHostTable(_ffiTable);

        // Compile FFI-heavy chunk
        string ffiHeavyScript =
            @"
var x = enemy.getX();
var z = enemy.getZ();
var time = enemy.getTime();
var offset = enemy.getOffset();
var angle = time * 0.8 + offset;
var newX = math.cos(angle) * 15.0;
var newZ = math.sin(angle) * 15.0;
enemy.setPosition(newX, newZ);
";
        string ffiHeavyRasm = Raptor.Compiler.RaptorScriptCompiler.Compile(ffiHeavyScript);
        _ffiHeavyChunk = _engine.Compile(ffiHeavyRasm);

        // Compile Property-Mapped chunk
        string propertyMappedScript =
            @"
var angle = enemy.time * 0.8 + enemy.offset;
enemy.x = math.cos(angle) * 15.0;
enemy.z = math.sin(angle) * 15.0;
";
        var propertyMappings = new Dictionary<string, int>
        {
            { "enemy.x", 1 },
            { "enemy.z", 2 },
            { "enemy.time", 3 },
            { "enemy.offset", 4 },
        };
        string mappedRasm = Raptor.Compiler.RaptorScriptCompiler.Compile(
            propertyMappedScript,
            propertyMappings
        );
        _propertyMappedChunk = _engine.Compile(mappedRasm);

        // Initialize single shared VM for ALL processing
        _sharedVM = new VirtualMachine();
        _sharedVM.RegisterHostTable(_ffiTable);
        _sharedVM.LoadProgram(_propertyMappedChunk);
        _lastMode = ExecutionMode.RaptorVM_PropertyMapped;

        SpawnAgents();
    }

    private void SpawnAgents()
    {
        _agentsArray = new Agent[spawnCount];

        // Setup Zero-Allocation GPU Batches (DrawMeshInstanced limits arrays to 1023 max)
        int maxBatchSize = 1023;
        int numBatches = Mathf.CeilToInt((float)spawnCount / maxBatchSize);
        _matrixBatches = new Matrix4x4[numBatches][];

        int side = Mathf.CeilToInt(Mathf.Sqrt(spawnCount));
        float spacing = 1.5f;

        for (int i = 0; i < numBatches; i++)
        {
            int size = Mathf.Min(maxBatchSize, spawnCount - (i * maxBatchSize));
            _matrixBatches[i] = new Matrix4x4[size];
        }

        for (int i = 0; i < spawnCount; i++)
        {
            int row = i / side;
            int col = i % side;

            _agentsArray[i] = new Agent
            {
                X = (col - (side / 2f)) * spacing,
                Z = (row - (side / 2f)) * spacing,
                SpeedOffset = UnityEngine.Random.Range(0f, 6.28f),
            };
        }
    }

    private void Update()
    {
        _currentTime = Time.time;

        // FPS calculation
        _fpsTimer += Time.deltaTime;
        _fpsCount++;
        if (_fpsTimer >= 0.5f)
        {
            _fps = _fpsCount / _fpsTimer;
            _fpsTimer = 0;
            _fpsCount = 0;
        }

        if (_agentsArray == null || _agentsArray.Length != spawnCount)
        {
            SpawnAgents();
        }

        // Switch active program chunk if mode changed
        if (_lastMode != mode)
        {
            if (mode == ExecutionMode.RaptorVM_FFI)
                _sharedVM.LoadProgram(_ffiHeavyChunk);
            else if (mode == ExecutionMode.RaptorVM_PropertyMapped)
                _sharedVM.LoadProgram(_propertyMappedChunk);
            _lastMode = mode;
        }

        // ==========================================
        // THE TIMED BENCHMARK BLOCK
        // ==========================================
        Stopwatch sw = Stopwatch.StartNew();

        if (mode == ExecutionMode.RaptorVM_FFI)
        {
            for (int i = 0; i < spawnCount; i++)
            {
                _tempX = _agentsArray[i].X;
                _tempZ = _agentsArray[i].Z;
                _tempOffset = _agentsArray[i].SpeedOffset;

                _sharedVM.RunFast(); // Re-uses the single VM, maintaining hot cache

                _agentsArray[i].X = _newX;
                _agentsArray[i].Z = _newZ;
            }
        }
        else if (mode == ExecutionMode.RaptorVM_PropertyMapped)
        {
            for (int i = 0; i < spawnCount; i++)
            {
                _sharedVM.SetRegister(1, _agentsArray[i].X);
                _sharedVM.SetRegister(2, _agentsArray[i].Z);
                _sharedVM.SetRegister(3, _currentTime);
                _sharedVM.SetRegister(4, _agentsArray[i].SpeedOffset);

                _sharedVM.RunFast();

                _agentsArray[i].X = _sharedVM.GetRegister(1);
                _agentsArray[i].Z = _sharedVM.GetRegister(2);
            }
        }
        else // Native C#
        {
            for (int i = 0; i < spawnCount; i++)
            {
                double angle = _currentTime * 0.8 + _agentsArray[i].SpeedOffset;
                _agentsArray[i].X = Math.Cos(angle) * 15.0;
                _agentsArray[i].Z = Math.Sin(angle) * 15.0;
            }
        }

        sw.Stop();
        _lastBatchExecutionTimeMs = (sw.ElapsedTicks / (double)Stopwatch.Frequency) * 1000.0;

        // ------------------------------------------
        // ZERO-ALLOCATION GPU RENDERING
        // ------------------------------------------
        if (renderMeshes && cubeMesh != null && cubeMaterial != null)
        {
            int batchIndex = 0;
            int elementIndex = 0;

            // Generate GPU Matrices directly from the flat array
            for (int i = 0; i < spawnCount; i++)
            {
                _matrixBatches[batchIndex][elementIndex] = Matrix4x4.Translate(
                    new Vector3((float)_agentsArray[i].X, 0, (float)_agentsArray[i].Z)
                );

                elementIndex++;
                if (elementIndex >= 1023)
                {
                    batchIndex++;
                    elementIndex = 0;
                }
            }

            // Blast batches to the GPU
            for (int i = 0; i < _matrixBatches.Length; i++)
            {
                Graphics.DrawMeshInstanced(cubeMesh, 0, cubeMaterial, _matrixBatches[i]);
            }
        }
    }

    // FFI Callbacks
    [RaptorMethod("getX")]
    public double GetAgentX() => _tempX;

    [RaptorMethod("getZ")]
    public double GetAgentZ() => _tempZ;

    [RaptorMethod("getTime")]
    public double GetTime() => _currentTime;

    [RaptorMethod("getOffset")]
    public double GetOffset() => _tempOffset;

    [RaptorMethod("setPosition")]
    public void SetAgentPosition(double x, double z)
    {
        _newX = x;
        _newZ = z;
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(10, 10, 330, 220), "Raptor VM Stress Test Benchmark");
        GUILayout.BeginArea(new Rect(20, 40, 310, 180));

        GUILayout.Label($"Active Instances: {spawnCount}");
        GUILayout.Label($"Execution Mode: <b>{mode}</b>");
        GUILayout.Label($"FPS: {_fps:F1}");

        GUILayout.Space(5);

        GUILayout.Label($"Total Logic Frame Time: <b>{_lastBatchExecutionTimeMs:F3} ms</b>");

        double avgUsPerInstance = (_lastBatchExecutionTimeMs * 1000.0) / spawnCount;
        GUILayout.Label($"Avg Time Per Script: <b>{avgUsPerInstance:F3} us</b>");

        GUILayout.Space(10);

        string nextModeName =
            mode == ExecutionMode.RaptorVM_PropertyMapped ? "Native C#"
            : mode == ExecutionMode.NativeCSharp ? "RaptorVM (FFI-Heavy)"
            : "RaptorVM (Property-Mapped)";

        ExecutionMode nextMode =
            mode == ExecutionMode.RaptorVM_PropertyMapped ? ExecutionMode.NativeCSharp
            : mode == ExecutionMode.NativeCSharp ? ExecutionMode.RaptorVM_FFI
            : ExecutionMode.RaptorVM_PropertyMapped;

        if (GUILayout.Button($"Switch to {nextModeName}"))
            mode = nextMode;
        if (GUILayout.Button($"Toggle Mesh Rendering (Renderer: {(renderMeshes ? "ON" : "OFF")})"))
            renderMeshes = !renderMeshes;

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("10k Cubes"))
            spawnCount = 10000;
        if (GUILayout.Button("50k Cubes"))
            spawnCount = 50000;
        if (GUILayout.Button("100k Cubes"))
            spawnCount = 100000;
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }
}
