﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class agentKart : MonoBehaviour
{
    [System.Serializable]
    public class StatPowerup
    {
        public agentKart.Stats modifiers;
        public string PowerUpID;
        public float ElapsedTime;
        public float MaxTime;
    }

    [System.Serializable]
    public struct Stats
    {
        [Header("Movement Settings")]
        [Tooltip("The maximum speed forwards")]
        public float TopSpeed;

        [Tooltip("How quickly the Kart reaches top speed.")]
        public float Acceleration;

        [Tooltip("The maximum speed backward.")]
        public float ReverseSpeed;

        [Tooltip("The rate at which the kart increases its backward speed.")]
        public float ReverseAcceleration;

        [Tooltip("How quickly the Kart starts accelerating from 0. A higher number means it accelerates faster sooner.")]
        [Range(0.2f, 1)]
        public float AccelerationCurve;

        [Tooltip("How quickly the Kart slows down when going in the opposite direction.")]
        public float Braking;

        [Tooltip("How quickly to slow down when neither acceleration or reverse is held.")]
        public float CoastingDrag;

        [Range(0, 1)]
        [Tooltip("The amount of side-to-side friction.")]
        public float Grip;

        [Tooltip("How quickly the Kart can turn left and right.")]
        public float Steer;

        [Tooltip("Additional gravity for when the Kart is in the air.")]
        public float AddedGravity;

        [Tooltip("How much the Kart tries to keep going forward when on bumpy terrain.")]
        [Range(0, 1)]
        public float Suspension;

        // allow for stat adding for powerups.
        public static Stats operator +(Stats a, Stats b) {
            return new Stats
            {
                Acceleration        = a.Acceleration + b.Acceleration,
                AccelerationCurve   = a.AccelerationCurve + b.AccelerationCurve,
                Braking             = a.Braking + b.Braking,
                CoastingDrag        = a.CoastingDrag + b.CoastingDrag,
                AddedGravity        = a.AddedGravity + b.AddedGravity,
                Grip                = a.Grip + b.Grip,
                ReverseAcceleration = a.ReverseAcceleration + b.ReverseAcceleration,
                ReverseSpeed        = a.ReverseSpeed + b.ReverseSpeed,
                TopSpeed            = a.TopSpeed + b.TopSpeed,
                Steer               = a.Steer + b.Steer,
                Suspension          = a.Suspension + b.Suspension
            };
        }
    }

    public Rigidbody Rigidbody { get; private set; }
    public Vector2 thisInput       { get; private set; }
    public float AirPercent    { get; private set; }
    public float GroundPercent { get; private set; }

    public agentKart.Stats baseStats = new agentKart.Stats
    {
        TopSpeed            = 10f,
        Acceleration        = 5f,
        AccelerationCurve   = 4f,
        Braking             = 10f,
        ReverseAcceleration = 5f,
        ReverseSpeed        = 5f,
        Steer               = 5f,
        CoastingDrag        = 4f,
        Grip                = .95f,
        AddedGravity        = 1f,
        Suspension          = .2f
    };

    [Header("Vehicle Physics")]
    [Tooltip("The transform that determines the position of the Kart's mass.")]
    public Transform CenterOfMass;

    [Tooltip("The physical representations of the Kart's wheels.")]
    public Transform[] Wheels;

    [Tooltip("Which layers the wheels will detect.")]
    public LayerMask GroundLayers = Physics.DefaultRaycastLayers;

    [Tooltip("How far to raycast when checking for ground.")]
    public float RaycastDist = 0.3f;

    [Tooltip("How high to keep the kart above the ground.")]
    public float MinHeightThreshold = 0.02f;

    public Transform SuspensionBody;

    // saved transforms of where the suspension's neutral positions are
    private Vector3 suspensionNeutralPos;
    private Quaternion suspensionNeutralRot;

    // can the kart move?
    private bool canMove = true;
    private List<StatPowerup> activePowerupList = new List<StatPowerup>();
    private GameObject lastGroundCollided = null;
    private agentKart.Stats finalStats;

    private bool toStopRace = false;

    private void Awake() {
        Rigidbody = GetComponent<Rigidbody>();
        suspensionNeutralPos = SuspensionBody.transform.localPosition;
        suspensionNeutralRot = SuspensionBody.transform.localRotation;
        baseStats.Braking = baseStats.TopSpeed;
    }

    private void FixedUpdate() {
        ResetIfStuck();

        toStopRace = toEnd();

        // apply our powerups to create our finalStats
        TickPowerups();

        // apply our physics properties
        Rigidbody.centerOfMass = Rigidbody.transform.InverseTransformPoint(CenterOfMass.position);

        // calculate how grounded and airborne we are
        int groundedCount = CountGroundedWheels(out float minHeight);
        GroundPercent = (float)groundedCount / Wheels.Length;
        AirPercent = 1 - GroundPercent;

        // gather inputs
        float accel = thisInput.y;
        float turn = thisInput.x;

        // apply vehicle physics
        GroundVehicle(minHeight);
        if (canMove)
        {
            MoveVehicle(accel, turn);
        }
        GroundAirbourne();

        // animation
        AnimateSuspension();
    }

    public bool getStopRace() {
        return toStopRace;
    }

    public void GatherInputs(float x, float y) {
        // reset input
        thisInput = Vector2.zero;

        Vector2 current = new Vector2(x, y);
        if (current.sqrMagnitude > 0)
        {
            thisInput = current;
        }
    }

    private void TickPowerups() {
        // remove all elapsed powerups
        activePowerupList.RemoveAll((p) => { return p.ElapsedTime > p.MaxTime; });

        // zero out powerups before we add them all up
        var powerups = new Stats();

        // add up all our powerups
        for (int i = 0; i < activePowerupList.Count; i++)
        {
            var p = activePowerupList[i];

            // add elapsed time
            p.ElapsedTime += Time.deltaTime;

            // add up the powerups
            powerups += p.modifiers;
        }

        // add powerups to our final stats
        finalStats = baseStats + powerups;

        // clamp values in finalstats
        finalStats.Grip = Mathf.Clamp(finalStats.Grip, 0, 1);
        finalStats.Suspension = Mathf.Clamp(finalStats.Suspension, 0, 1);
    }

    private void AnimateSuspension() {
        // simple suspension animation
        var suspensionTargetPos = suspensionNeutralPos;
        var suspensionTargetRot = suspensionNeutralRot;
        var bodyRot = transform.rotation.eulerAngles;

        var maxXTilt = finalStats.Suspension * 45;
        var closestNeutralRot = Mathf.Abs(360 - bodyRot.x) < Mathf.Abs(bodyRot.x) ? 360 : 0;
        var xTilt = Mathf.DeltaAngle(closestNeutralRot, bodyRot.x);

        var suspensionT = Mathf.InverseLerp(0, maxXTilt, xTilt);
        suspensionT = suspensionT * suspensionT;

        //Debug.Log("Suspension: " + suspensionT + " bodyRot: "  + bodyRot.x + " neutral: " + closestNeutralRot);
        bodyRot.x = Mathf.Lerp(closestNeutralRot, bodyRot.x, suspensionT);

        // transform bodyRot to suspension local space
        suspensionTargetRot = Quaternion.Inverse(SuspensionBody.transform.rotation) * Quaternion.Euler(bodyRot);

        // apply the new transforms
        SuspensionBody.transform.localPosition = Vector3.Lerp(SuspensionBody.transform.localPosition, suspensionTargetPos, Time.deltaTime * 5f);
        SuspensionBody.transform.localRotation = Quaternion.Slerp(SuspensionBody.transform.localRotation, suspensionTargetRot, Time.deltaTime * 5f);
    }

    private int CountGroundedWheels(out float minHeight) {
        int groundedCount = 0;
        minHeight = float.MaxValue;

        for (int i = 0; i < Wheels.Length; i++) {
            Transform current = Wheels[i];
            groundedCount += Physics.Raycast(current.position, Vector3.down, out RaycastHit hit, RaycastDist) ? 1 : 0;

            if (hit.distance > 0) {
                minHeight = Mathf.Min(hit.distance, minHeight);
            }
        }

        return groundedCount;
    }

    private void GroundVehicle(float minHeight) {
        if (GroundPercent >= 1f) {
            if (minHeight < MinHeightThreshold) {
                float diff = MinHeightThreshold - minHeight;
                transform.position += diff * transform.up;
            }
        }
    }

    private void GroundAirbourne() {
        // while in the air, fall faster
        if (AirPercent >= 1) {
            Rigidbody.velocity += Physics.gravity * Time.deltaTime * finalStats.AddedGravity;
        }
    }

    private void MoveVehicle(float accelInput, float turnInput) {
        // manual acceleration curve coefficient scalar
        float accelerationCurveCoeff = 5;
        Vector3 localVel = transform.InverseTransformVector(Rigidbody.velocity);

        bool accelDirectionIsFwd = accelInput >= 0;
        bool localVelDirectionIsFwd = localVel.z >= 0;

        // use the max speed for the direction we are going--forward or reverse.
        float maxSpeed = accelDirectionIsFwd ? finalStats.TopSpeed : finalStats.ReverseSpeed;
        float accelPower = accelDirectionIsFwd ? finalStats.Acceleration : finalStats.ReverseAcceleration;

        float accelRampT = Rigidbody.velocity.magnitude / maxSpeed;
        float multipliedAccelerationCurve = finalStats.AccelerationCurve * accelerationCurveCoeff;
        float accelRamp = Mathf.Lerp(multipliedAccelerationCurve, 1, accelRampT * accelRampT);

        bool isBraking = accelDirectionIsFwd != localVelDirectionIsFwd;

        // if we are braking (moving reverse to where we are going)
        // use the braking accleration instead
        float finalAccelPower = isBraking ? finalStats.Braking : accelPower;

        float finalAcceleration = finalAccelPower * accelRamp;

        // apply inputs to forward/backward
        float turningPower = turnInput * finalStats.Steer;

        Quaternion turnAngle = Quaternion.AngleAxis(turningPower, Rigidbody.transform.up);
        Vector3 fwd = turnAngle * Rigidbody.transform.forward;

        Vector3 movement = fwd * accelInput * finalAcceleration * GroundPercent;

        // simple suspension allows us to thrust forward even when on bumpy terrain
        fwd.y = Mathf.Lerp(fwd.y, 0, finalStats.Suspension);

        // forward movement
        float currentSpeed = Rigidbody.velocity.magnitude;
        bool wasOverMaxSpeed = currentSpeed >= maxSpeed;

        // if over max speed, cannot accelerate faster.
        if (wasOverMaxSpeed && !isBraking) movement *= 0;

        Vector3 adjustedVelocity = Rigidbody.velocity + movement * Time.deltaTime;

        adjustedVelocity.y = Rigidbody.velocity.y;

        //  clamp max speed if we are on ground
        if (GroundPercent > 0) {
            if (adjustedVelocity.magnitude > maxSpeed && !wasOverMaxSpeed) {
                adjustedVelocity = Vector3.ClampMagnitude(adjustedVelocity, maxSpeed);
            }
        }

        // coasting is when we aren't touching accelerate
        bool isCoasting = Mathf.Abs(accelInput) < .01f;

        if (isCoasting) {
            Vector3 restVelocity = new Vector3(0, Rigidbody.velocity.y, 0);
            adjustedVelocity = Vector3.MoveTowards(adjustedVelocity, restVelocity, Time.deltaTime * finalStats.CoastingDrag);
        }

        Rigidbody.velocity = adjustedVelocity;

        ApplyAngularSuspension();

        if (GroundPercent > 0) {
            // manual angular velocity coefficient
            float angularVelocitySteering = .4f;
            float angularVelocitySmoothSpeed = 20f;

            // turning is reversed if we're going in reverse and pressing reverse
            if (!localVelDirectionIsFwd && !accelDirectionIsFwd) angularVelocitySteering *= -1;
            var angularVel = Rigidbody.angularVelocity;

            // move the Y angular velocity towards our target
            angularVel.y = Mathf.MoveTowards(angularVel.y, turningPower * angularVelocitySteering, Time.deltaTime * angularVelocitySmoothSpeed);

            // apply the angular velocity
            Rigidbody.angularVelocity = angularVel;

            // rotate rigidbody's velocity as well to generate immediate velocity redirection
            // manual velocity steering coefficient
            float velocitySteering = 25f;
            // rotate our velocity based on current steer value
            Rigidbody.velocity = Quaternion.Euler(0f, turningPower * velocitySteering * Time.deltaTime, 0f) * Rigidbody.velocity;
        }

        // apply simplified lateral ground friction
        // only apply if we are on the ground at all
        if (GroundPercent > 0f) {
            // manual grip coefficient scalar
            float gripCoeff = 30f;
            // what direction is our lateral friction in?
            // it is the direction the wheels are turned, our forward
            Vector3 latFrictionDirection = Vector3.Cross(fwd, transform.up);
            // how fast are we currently moving in our friction direction?
            float latSpeed = Vector3.Dot(Rigidbody.velocity, latFrictionDirection);
            // apply the damping
            Vector3 latFrictionDampedVelocity = Rigidbody.velocity - latFrictionDirection * latSpeed * finalStats.Grip * gripCoeff * Time.deltaTime;

            // apply the damped velocity
            Rigidbody.velocity = latFrictionDampedVelocity;
        }
    }

    private void ApplyAngularSuspension() {
        // simple suspension dampens x and z angular velocity while on the ground
        Vector3 suspendedX = transform.right;
        Vector3 suspendedZ = transform.forward;
        suspendedX.y *= 0f;
        suspendedZ.y *= 0f;
        var sX = Vector3.Dot(Rigidbody.angularVelocity, suspendedX) * suspendedX;
        var sZ = Vector3.Dot(Rigidbody.angularVelocity, suspendedZ) * suspendedZ;
        var sXZ = sX + sZ;
        var sCoeff = 10f;

        Vector3 suspensionRotation;
        float minimumSuspension = 0.5f;
        if (GroundPercent > 0.5f || finalStats.Suspension < minimumSuspension) {
            suspensionRotation = sXZ * finalStats.Suspension * sCoeff * Time.deltaTime;
        } else {
            suspensionRotation = sXZ * minimumSuspension * sCoeff * Time.deltaTime;
        }

        Vector3 suspendedAngular = Rigidbody.angularVelocity - suspensionRotation;

        // apply the adjusted angularvelocity
        Rigidbody.angularVelocity = suspendedAngular;
    }

    private bool IsStuck() {
        float speed = Rigidbody.velocity.magnitude;
        if(GroundPercent <= 0 && speed < 0.01f && Mathf.Abs(thisInput.y) > 0)
            return true;

        return false;
    }

    private bool isFalling() {
        if (gameObject.transform.localPosition.y < 0f) {
            return true;
        }
        return false;
    }

    private bool isUpsideDown() {
        if (Vector3.Dot(transform.up, Vector3.down) > 0f) {
            return true;
        }
        return false;
    }

    private void OnCollisionEnter(Collision other) {
        if(GroundLayers == (GroundLayers | (1 << other.collider.gameObject.layer))) {
            lastGroundCollided = other.collider.gameObject;
        }
    }

    private void ResetIfStuck() {
        if (IsStuck() && lastGroundCollided != null) {
            if (lastGroundCollided.TryGetComponent(out Collider collider)) {
                Vector3 pos = new Vector3(
                    collider.bounds.center.x,
                    collider.bounds.max.y,
                    collider.bounds.center.z
                );
                transform.position = pos;
            }
        }
    }

    private bool toEnd() {
        if (isFalling() || isUpsideDown()) {
            return true;
        }
        return false;
    }

    public void AddPowerup(StatPowerup statPowerup) {
        activePowerupList.Add(statPowerup);
    }

    public void Reset() {
        Vector3 euler      = transform.rotation.eulerAngles;
        euler.x            = euler.z = 0f;
        transform.rotation = Quaternion.Euler(euler);
    }

    public float LocalSpeed() {
        if (canMove) {
            float dot = Vector3.Dot(transform.forward, Rigidbody.velocity);
            if (Mathf.Abs(dot) > 0.1f) {
                float speed = Rigidbody.velocity.magnitude;
                return dot < 0 ? -(speed / finalStats.ReverseSpeed) : (speed / finalStats.TopSpeed);
            }
            return 0f;
        } else {
            // use this value to play kart sound when it is waiting the race start countdown.
            return thisInput.y;
        }
    }

    public void SetCanMove(bool move) {
        canMove = move;
    }

    public void setTopSpeed(float newTopSpeed) {
        baseStats.TopSpeed = newTopSpeed;
        baseStats.Braking = newTopSpeed;
    }
}
