using UnityEngine;
using UnityEngine.InputSystem;

public class BedHandler : MonoBehaviour
{
    [SerializeField]
    InputActionReference startNewWave;

    [SerializeField]
    float minInteractDistance = 3f;

    [SerializeField]
    GameObject interactVisuals;

    [SerializeField]
    ParticleSystem waveStartParticles;

    // Update is called once per frame
    void Update()
    {
        Vector2 playerPos = PlayerController.player.transform.position;
        float distance = Vector2.Distance(playerPos, transform.position);
        if (distance <= minInteractDistance) {
            interactVisuals.SetActive(true);
            if(EnemySpawner.instance.currentEnemies.Count == 0) {
                if (startNewWave.action.triggered) {

                    if (RemoraDevConsole.GameplayInputBlocked()) { return; }

                    waveStartParticles.Play();
                    EnemySpawner.instance.SpawnWave();
                }
            } else {
                interactVisuals.SetActive(false);
                //Debug.Log("Enemies still alive");
            }
        } else {
            interactVisuals.SetActive(false);
        }
    }

    private void OnDrawGizmos() {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, minInteractDistance);
    }
}
