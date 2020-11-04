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

    public bool isReversed = false;   

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

        if (Input.GetKeyDown(KeyCode.DownArrow)) {
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
        //Debug.Log($"STABILIZATION: {rotCounter}");
        if ((rotCounter >= 2) && (rotCounter > 0)) {
            rotCounter -= 2;
        } else if ((rotCounter <= -2) && (rotCounter < 0)) {
            rotCounter += 2;
        } else if ((rotCounter > -2) && (rotCounter < 2)) {
            rotCounter = 0;
        }
    }

    public void Flip() {
        rb.transform.localRotation *= Quaternion.Euler(0, 180, 0);
        isReversed = !isReversed;
    }
}
