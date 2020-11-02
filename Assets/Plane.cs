using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Plane : MonoBehaviour
{
    public Rigidbody2D rb;

    public float speed = 10f;

    // private
    float rotCounter;
    

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    // trigger movement
    void Update() {
        // rotation amount/movement
        if (Input.GetKey(KeyCode.LeftArrow)) {
            rotCounter += 1;
        }
        if (Input.GetKey(KeyCode.RightArrow)) {
            rotCounter -= 1;
        }
    }

    // something with physics
    void FixedUpdate()
    {
        rb.velocity = rb.transform.right * speed;

        float rotAmount = rotCounter * Time.deltaTime;
        float curRot = transform.localRotation.eulerAngles.z;
        transform.localRotation = Quaternion.Euler(new Vector3(0, 0, curRot + rotAmount));

        // stabilization
        //Debug.Log($"STABILIZATION: {rotCounter}");

        if ((rotCounter >= 2) && (rotCounter > 0)) {
            rotCounter -= 2;
        } else if ((rotCounter <= -2) && (rotCounter < 0)) {
            rotCounter += 2;
        } else if ((rotCounter > -2) && (rotCounter < 2)) {
            rotCounter = 0;
        }
    }
}

/*
    Screen object movement

    Update()
    movement.x = Input.GetAxisRaw("Horizontal");
    movement.y = Input.GetAxisRaw("Vertical");

    FixedUpdate()
    rb.MovePosition(rb.position + movement * moveSpeed * Time.fixedDeltaTime)
*/