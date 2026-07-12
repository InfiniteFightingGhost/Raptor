using UnityEngine;
using Raptor;

/*
  =============================================================================
   Example RaptorScript Source (save this file as Assets/Scripts/enemy_ai.rapt)
  =============================================================================
  
  var targetDistance = enemy.getDistanceToPlayer();
  var state = enemy.getState(); // 0 = Idle, 1 = Chasing, 2 = Attacking
  
  if (targetDistance < 5.0) {
      // Transition to Attacking state
      enemy.setState(2.0);
      
      var cooldown = enemy.getAttackCooldown();
      if (cooldown == 0.0) {
          enemy.attackPlayer(15.0);     // Deal 15 damage
          enemy.setAttackCooldown(3.0);  // 3 second cooldown
      } else {
          // Reduce cooldown by deltaTime (desugared -= subtraction)
          cooldown -= 0.016; 
          if (cooldown < 0.0) {
              cooldown = 0.0;
          }
          enemy.setAttackCooldown(cooldown);
      }
  } else {
      if (targetDistance < 20.0) {
          // Player detected, Chase
          enemy.setState(1.0);
          enemy.moveTowardsPlayer(8.0); // Run speed
      } else {
          // Player out of range, Patrol
          enemy.setState(0.0);
          enemy.patrol(3.0);            // Walk speed
      }
  }

  =============================================================================
*/

/// <summary>
/// A real-world example of how to execute a complex C-like RaptorScript (.rapt) file
/// on a Unity GameObject to run AI behaviors.
/// </summary>
public class RaptorScriptGameplayExample : MonoBehaviour
{
    [Header("Script Settings")]
    [Tooltip("Path to the C-like .rapt file relative to the project directory.")]
    public string raptorScriptPath = "Assets/Scripts/enemy_ai.rapt";

    [Header("Enemy Attributes")]
    public Transform playerTransform;
    public float walkSpeed = 3.0f;
    public float runSpeed = 8.0f;

    private int _aiState = 0; // 0 = Idle, 1 = Chasing, 2 = Attacking
    private double _attackCooldown = 0.0;
    
    private ScriptEngine _engine;
    private ScriptWatcher _watcher;

    private void Awake()
    {
        // 1. Setup the FFI Table and register AI APIs
        var table = new FFIHostTable();
        table.RegisterModule(this); // Register this instance as FFI module

        // 2. Setup the compiler engine
        _engine = new ScriptEngine();
        _engine.RegisterHostTable(table);

        // 3. Compile the .rapt script file and monitor it for hot reloading
        try
        {
            // Convert .rapt source to .rasm assembly, then compile to VM chunk
            string rasmAssembly = Raptor.Compiler.RaptorScriptCompiler.Compile(
                System.IO.File.ReadAllText(raptorScriptPath)
            );
            
            // Create a temporary .rasm file for the watcher to monitor
            string tempRasmPath = raptorScriptPath.Replace(".rapt", ".rasm");
            System.IO.File.WriteAllText(tempRasmPath, rasmAssembly);

            _watcher = new ScriptWatcher(_engine, tempRasmPath);
            
            _watcher.OnReloaded += (chunk) => Debug.Log("[Raptor AI] AI script hot-reloaded successfully.");
            _watcher.OnReloadError += (ex) => Debug.LogError($"[Raptor AI Compiler Error] {ex.Message}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Raptor AI Init Error] Failed to initialize AI compiler: {ex.Message}");
        }
    }

    private void Update()
    {
        if (_watcher == null || playerTransform == null) return;

        // 4. Run the AI Script VM chunk on every frame
        try
        {
            _engine.Execute(_watcher.ActiveChunk);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Raptor AI Runtime Error] {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        _watcher?.Dispose();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  FFI APIs exposed to the C-like RaptorScript (.rapt) under the "enemy" module
    // ────────────────────────────────────────────────────────────────────────

    [RaptorMethod("getDistanceToPlayer")]
    public double GetDistanceToPlayer()
    {
        if (playerTransform == null) return 999.0;
        return Vector3.Distance(transform.position, playerTransform.position);
    }

    [RaptorMethod("getState")]
    public double GetState() => _aiState;

    [RaptorMethod("setState")]
    public void SetState(double state)
    {
        _aiState = (int)state;
    }

    [RaptorMethod("getAttackCooldown")]
    public double GetAttackCooldown() => _attackCooldown;

    [RaptorMethod("setAttackCooldown")]
    public void SetAttackCooldown(double cooldown)
    {
        _attackCooldown = cooldown;
    }

    [RaptorMethod("attackPlayer")]
    public void AttackPlayer(double damage)
    {
        Debug.Log($"[Enemy AI] *SLASH* Dealt {damage} damage to player!");
    }

    [RaptorMethod("moveTowardsPlayer")]
    public void MoveTowardsPlayer(double speed)
    {
        Vector3 direction = (playerTransform.position - transform.position).normalized;
        transform.position += direction * (float)speed * Time.deltaTime;
        Debug.Log($"[Enemy AI] Chasing player at speed: {speed}");
    }

    [RaptorMethod("patrol")]
    public void Patrol(double speed)
    {
        // Simple patrol left-and-right simulation
        float offset = Mathf.PingPong(Time.time * (float)speed, 6.0f) - 3.0f;
        transform.position = new Vector3(offset, transform.position.y, transform.position.z);
        Debug.Log($"[Enemy AI] Patrolling at speed: {speed}");
    }
}
