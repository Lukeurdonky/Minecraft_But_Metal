using Godot;
using System;
using System.Collections.Generic;

public partial class Creature : Entity
{
    [Export]
    public float WalkSpeed { get; set; } = 3.0f;

    [Export]
    public float ChaseSpeed { get; set; } = 6.0f;

    [Export]
    public float DetectionRange { get; set; } = 15.0f;

    private Vector3 targetPosition = Vector3.Zero;
    private bool isChasing = false;

    public override void ApplyMovementFromInput(double delta)
    {

        Vector3 playerPos = Global.GetPlayerPos();
        float distanceToPlayer = (playerPos - GlobalTransform.Origin).Length();

        if (distanceToPlayer <= DetectionRange)
        {
            isChasing = true;
            targetPosition = playerPos;
        }
        else
        {
            isChasing = false;
        }

        if (isChasing)
        {
            Vector3 directionToPlayer = (targetPosition - GlobalTransform.Origin).Normalized();
            Velocity = directionToPlayer * ChaseSpeed;
        }
        else
        {
            // Simple wandering logic could go here
            Velocity = Vector3.Zero;
        }
    }
}