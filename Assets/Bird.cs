using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Bird : MonoBehaviour
{
    public Rigidbody2D rb;

    public float speed = 5f;

    // private
    float rotCounter;

    public bool isReversed = false;   

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    // trigger movement
    void Update() {
        // rotation amount/movement
        if (Input.GetKey(KeyCode.A)) {
            rotCounter += 30;
        }
        if (Input.GetKey(KeyCode.D)) {
            rotCounter -= 30;
        }

        if (Input.GetKeyDown(KeyCode.S)) {
            Debug.Log($"DOWN {rb.transform.localRotation}");
            Flip();
        }
    }

    // something with physics
    void FixedUpdate()
    {
        // movement
        rb.velocity = rb.transform.right * speed;

        // rotation
        float rotAmount = rotCounter * Time.deltaTime;
        float curRot = transform.localRotation.eulerAngles.z;
        if (isReversed) {
            transform.localRotation = Quaternion.Euler(new Vector3(0, 180, curRot + rotAmount));
        } else {
            transform.localRotation = Quaternion.Euler(new Vector3(0, 0, curRot + rotAmount));
        }

        // stabilization
        Debug.Log($"STABILIZATION: {rotCounter}");
        // if ((rotCounter >= 2) && (rotCounter > 0)) {
        //     rotCounter -= 2;
        // } else if ((rotCounter <= -2) && (rotCounter < 0)) {
        //     rotCounter += 2;
        // } else if ((rotCounter > -2) && (rotCounter < 2)) {
        //     rotCounter = 0;
        // }
        rotCounter = 0;

        // engines on on 270 rotation
        //Debug.Log($"ROTATION: {curRot}");
        //Debug.Log($"ROTATION: {rotCounter}");
        Debug.Log($"SPEED: {speed}");
        Debug.Log($"GRAVITY: {rb.gravityScale}");

        if (speed == 0 && (curRot > 265 && curRot < 270)) {
            EngineOn();
        }
        // speedup on head down
        if (curRot > 265 && curRot < 270) {
            speed += 1;
        }
    }

    public void Flip() {
        rb.transform.localRotation *= Quaternion.Euler(0, 180, 0);
        isReversed = !isReversed;

        // back from edge with normalized values
        if (isReversed) {
            transform.localRotation = Quaternion.Euler(new Vector3(0, 180, 0));
        } else {
            transform.localRotation = Quaternion.Euler(new Vector3(0, 0, 0));
        }
        speed = 5;
    }

    public void EngineOff() {
        speed = 0;
        rb.gravityScale = 5;
    }

    public void EngineOn() {
        speed = 5;
        rb.gravityScale = 0;
    }
}
