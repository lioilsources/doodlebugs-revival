using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class PlaneBehaviour : NetworkBehaviour
{
    //public Camera cam;
	public Transform plane;
    public Transform leftPoint, rightPoint, forwardPoint;
	Rigidbody2D rb;
	public float speed = 5f, rotateSpeed = 50f;

    // debug
    //public Transform cloud;

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void FixedUpdate()
    {
        movePlane();
        rotatePlane(Input.GetAxis("Horizontal"));

        // Debug
        Debug.DrawRay(rb.position, DevMath.CalculateDirection(leftPoint.position, rb.position), Color.red);
        Debug.DrawRay(rb.position, DevMath.CalculateDirection(rightPoint.position, rb.position), Color.green);
        Debug.DrawRay(rb.position, DevMath.CalculateDirection(forwardPoint.position, rb.position), Color.blue);

        // var cloudDirection = DevMath.CalculateDirection(cloud.position, rb.position);
        // Debug.Log("Cloud direction: " + cloudDirection);
        // Debug.DrawRay(rb.position, cloudDirection, Color.white);

        //Debug.Log("Plane position: " + rb.position);
    }

    void movePlane(){
		rb.velocity = transform.right * speed;
	}

	void rotatePlane(float x){	
		float angle;
		Vector2 direction = new Vector2(0, 0);

        // turn left/right
		if (x < 0) 
        {
            direction = (Vector2) leftPoint.position - rb.position;
        }
		if (x > 0) {
            direction = (Vector2) rightPoint.position - rb.position;
        }

		direction.Normalize();
		angle = Vector3.Cross(direction, transform.up).z;
		
        // turn on/off
        if(x != 0) {
            rb.angularVelocity = -rotateSpeed * angle;
        } else {
            rb.angularVelocity = 0;
        }

		angle = Mathf.Atan2(
            forwardPoint.position.y - plane.transform.position.y, 
            forwardPoint.position.x - plane.transform.position.x
        ) * Mathf.Rad2Deg;
		
        plane.transform.rotation = Quaternion.Euler(new Vector3(0, 0, angle));
	}
}
