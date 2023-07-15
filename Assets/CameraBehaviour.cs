using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraBehaviour : MonoBehaviour
{
    public Transform target;
    public float speed = 5f;
    Vector3 offset;

    // Start is called before the first frame update
    void Start () {
		offset = target.position - transform.position;
		StartCoroutine(startAnimation());
	}

    // Update is called once per frame
	void LateUpdate () {
		transform.position = Vector3.Lerp(transform.position, target.position - offset, speed * Time.deltaTime);
	}

    IEnumerator startAnimation(){
		yield return new WaitForSeconds(2f);
	}
}
