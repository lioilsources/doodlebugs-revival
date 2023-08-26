using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class PlayerNetwork : NetworkBehaviour
{
    private void Update() {
        if (!IsOwner) return;

        Vector3 moveDir = new(0, 0, 0);

        if (Input.GetKey(KeyCode.UpArrow)) moveDir.y = +1f;
        if (Input.GetKey(KeyCode.DownArrow)) moveDir.y = -1f;
        if (Input.GetKey(KeyCode.LeftArrow)) moveDir.x = -1f;
        if (Input.GetKey(KeyCode.RightArrow)) moveDir.x = +1f;

        float moveSpeed = 3f;
        transform.position += moveSpeed * Time.deltaTime * moveDir;
    }
}
