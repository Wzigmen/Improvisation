using UnityEngine;

// The "mind" of a bot fighter. It only decides what buttons to press; PlayerController does the rest,
// so a bot moves, punches, gets knocked back and loses health exactly like a player.
// It runs on the host (the bot is owned by the host) and is deliberately a bit slower than a human.
public class BotBrain : MonoBehaviour
{
    [SerializeField] float attackRange = 1.9f;      // punches when the target is this close
    [SerializeField] float minAttackDelay = 0.8f;   // a human can punch every 0.5 s
    [SerializeField] float maxAttackDelay = 1.4f;
    [SerializeField] float firstAttackDelay = 0.7f; // reaction time when the fight starts

    PlayerController target;
    float nextRetarget;
    float nextAttack;
    float strafeSign = 1f;
    float nextStrafeFlip;
    bool wasActive;

    // Called every frame by PlayerController. `move` is a world-space direction (length 0..1),
    // `aim` where to face and punch, `attack` a "button press" this frame.
    public void Think(PlayerController self, bool active, out Vector3 move, out Vector3 aim, out bool attack)
    {
        move = Vector3.zero;
        aim = self.transform.forward;
        attack = false;

        if (!active)
        {
            wasActive = false;
            return;
        }
        if (!wasActive)
        {
            wasActive = true;
            nextAttack = Time.time + firstAttackDelay;
            nextStrafeFlip = Time.time + Random.Range(1.2f, 2.5f);
        }

        if (Time.time >= nextRetarget || target == null || target.Health <= 0)
        {
            nextRetarget = Time.time + 0.4f;
            target = FindNearestOpponent(self);
        }
        if (target == null) return;

        Vector3 toTarget = target.transform.position - self.transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        if (distance < 0.01f) return;

        Vector3 direction = toTarget / distance;
        aim = direction;

        if (Time.time >= nextStrafeFlip)
        {
            strafeSign = -strafeSign;
            nextStrafeFlip = Time.time + Random.Range(1.2f, 2.8f);
        }

        if (distance > attackRange * 0.9f)
        {
            move = direction; // close the gap
        }
        else
        {
            // In range: circle around instead of standing still, so it is harder to hit.
            Vector3 side = Vector3.Cross(Vector3.up, direction) * strafeSign;
            move = side * 0.5f;
        }

        if (distance <= attackRange && Time.time >= nextAttack)
        {
            attack = true;
            nextAttack = Time.time + Random.Range(minAttackDelay, maxAttackDelay);
        }
    }

    static PlayerController FindNearestOpponent(PlayerController self)
    {
        PlayerController best = null;
        float bestDistance = float.MaxValue;
        foreach (var p in MatchManager.ActivePlayers(true))
        {
            if (p == self || p.Health <= 0) continue;
            float d = (p.transform.position - self.transform.position).sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = p;
            }
        }
        return best;
    }
}
