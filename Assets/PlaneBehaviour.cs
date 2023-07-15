using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlaneBehaviour : MonoBehaviour
{
    public Camera cam;
	public Transform plane;
    public Transform leftPoint, rightPoint, forwardPoint;
	Rigidbody2D rb;
	public float speed = 5f, rotateSpeed = 50f;

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        movePlane();
        rotatePlane(Input.GetAxis("Horizontal"));
    }

    void movePlane(){
		rb.velocity = transform.up * speed;
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
