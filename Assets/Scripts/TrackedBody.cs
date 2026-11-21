using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where a tracked person actually is on the floor.
///
/// The ZED avatar's top object (the one tagged Player) doesn't follow the person reliably:
/// the ZED moves the avatar's BODY (hips, legs, feet). So for an avatar this reads the hips
/// (UseHips, the default: steadiest) or the point between the feet.
///
/// It looks for the body in two ways:
///   1. A humanoid Animator on the avatar (or on its parent): asks it for the Hips / Foot bones.
///   2. Otherwise, a child object whose name contains "hips" or "pelvis" (and "foot"/"ankle").
/// For anything without a body, like the laptop test cylinder, it uses the object's own position.
///
/// Only reads the avatar; the ZED plugin is not changed. No need to edit this script.
/// </summary>
public static class TrackedBody
{
    private class Body { public Transform hips, leftFoot, rightFoot; public string found; }

    private static readonly Dictionary<Transform, Body> cache = new Dictionary<Transform, Body>();

    /// <summary>Read the hips instead of the feet. Set by FloorCalibrator, and by CalibratedVisitors from the saved calibration.</summary>
    public static bool UseHips = true;

    /// <summary>Which part was read last time, for the Inspector readouts and the walk test.</summary>
    public static string LastPartUsed { get; private set; } = "";

    public static Vector3 FloorPoint(Transform player)
    {
        if (player == null) return Vector3.zero;

        if (!cache.TryGetValue(player, out Body body) || (body.hips == null && body.leftFoot == null))
        {
            body = Find(player);
            cache[player] = body;
        }

        if (UseHips && body.hips != null) { LastPartUsed = "hips (" + body.found + ")"; return body.hips.position; }
        if (body.leftFoot != null && body.rightFoot != null)
        {
            LastPartUsed = "feet (" + body.found + ")";
            return (body.leftFoot.position + body.rightFoot.position) * 0.5f;
        }
        if (body.hips != null) { LastPartUsed = "hips (" + body.found + ")"; return body.hips.position; }

        LastPartUsed = "object - no body found on " + player.name;
        return player.position;
    }

    private static Body Find(Transform player)
    {
        Body b = new Body();

        // 1. Humanoid Animator, on the avatar or above it.
        Animator animator = player.GetComponentInChildren<Animator>(true);
        if (animator == null || !animator.isHuman) animator = player.GetComponentInParent<Animator>();
        if (animator != null && animator.isHuman)
        {
            b.hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            b.leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            b.rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            b.found = "animator";
            if (b.hips != null || b.leftFoot != null) return b;
        }

        // 2. By name, anywhere in the avatar (start from its top-most object).
        Transform top = player;
        if (animator != null && animator.transform != player && player.IsChildOf(animator.transform)) top = animator.transform;
        foreach (Transform t in top.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (b.hips == null && (n.Contains("hips") || n.Contains("pelvis"))) b.hips = t;
            else if (b.leftFoot == null && (n.Contains("left") || n.Contains("_l") || n.StartsWith("l_")) && (n.Contains("foot") || n.Contains("ankle"))) b.leftFoot = t;
            else if (b.rightFoot == null && (n.Contains("right") || n.Contains("_r") || n.StartsWith("r_")) && (n.Contains("foot") || n.Contains("ankle"))) b.rightFoot = t;
        }
        b.found = "by name";
        return b;
    }

    /// <summary>Forget people who have gone (their objects were destroyed).</summary>
    public static void Tidy()
    {
        List<Transform> dead = null;
        foreach (Transform t in cache.Keys)
            if (t == null) (dead ??= new List<Transform>()).Add(t);
        if (dead != null) foreach (Transform t in dead) cache.Remove(t);
    }

    /// <summary>For checking: lists the object names inside an avatar (used by FloorCalibrator's menu).</summary>
    public static string DescribeHierarchy(Transform player, int maxLines = 60)
    {
        var sb = new System.Text.StringBuilder();
        int lines = 0;
        foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
        {
            if (lines++ >= maxLines) { sb.Append("..."); break; }
            sb.AppendLine(t.name + (t.GetComponent<Animator>() != null ? "  [Animator" + (t.GetComponent<Animator>().isHuman ? ", humanoid]" : ", NOT humanoid]") : ""));
        }
        return sb.ToString();
    }
}