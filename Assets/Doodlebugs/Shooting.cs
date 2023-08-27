using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

public class Shooting : NetworkBehaviour
{
    public Transform firePoint;
    public GameObject bulletPrefab;
    public float bulletForce = 20f;

    // Update is called once per frame
    void Update()
    {
        if (!IsOwner) return;

        if (Input.GetKeyDown(KeyCode.Space)) {
            ShootServerRpc();
            Debug.Log("Space");
        }
    }

    [ServerRpc]
    void ShootServerRpc() {
        var bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        bullet.GetComponent<NetworkObject>().Spawn(true);

        var rb = bullet.GetComponent<Rigidbody2D>();
        rb.AddForce(firePoint.right * bulletForce, ForceMode2D.Impulse);
    }
}
