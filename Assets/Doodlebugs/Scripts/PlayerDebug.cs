using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class PlaneDebug : MonoBehaviour
{
    public Transform leftPoint, rightPoint, forwardPoint;
    private Rigidbody2D rb;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void FixedUpdate()
    {
        Debug.DrawRay(rb.position, DevMath.CalculateDirection(leftPoint.position, rb.position), Color.red);
        Debug.DrawRay(rb.position, DevMath.CalculateDirection(rightPoint.position, rb.position), Color.green);
        Debug.DrawRay(rb.position, DevMath.CalculateDirection(forwardPoint.position, rb.position), Color.blue);

        // var cloudDirection = DevMath.CalculateDirection(cloud.position, rb.position);
        // Debug.Log("Cloud direction: " + cloudDirection);
        // Debug.DrawRay(rb.position, cloudDirection, Color.white);
    }
}
