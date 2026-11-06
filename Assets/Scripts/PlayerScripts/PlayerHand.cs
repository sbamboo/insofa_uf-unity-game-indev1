using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PlayerHand : MonoBehaviour {
    /*
    private static PlayerHand instance;
    public static PlayerHand Instance {  get { return instance; } }
    */


    LocalSoundComposer lsc;

    [SerializeField]
    public float GlobalBuyInflationMultiplier = 1;
    [SerializeField]
    public float GlobalSellInflationMultiplier = 1;
    [SerializeField]
    public float GlobalPickupInflationMultiplier = 1;

    // Reference to mainGameTokenIconResolver
    [SerializeField]
    public MainGame_TokenIconResolver mainGameTokenIconResolver;

    [SerializeField]
    int pillows = 5;

    [SerializeField]
    TextMeshProUGUI pillowText;

    Camera cam;

    [SerializeField]
    public BuildingScriptableObject[] buildings;

    int selectedBuilding = 0;

    Vector2 buildPos;

    [SerializeField]
    float buildRange = 1f;

    [SerializeField]
    Transform buildIndicator, attackIndicator, placementIndicator, buildingIndicator;


    [SerializeField]
    float collisionRadius;

    Vector2 lastLookDir = Vector2.right;
    Vector2 lookDir;
    PlayerInput pi;

    [SerializeField]
    InputActionReference lookInput;

    [Header("Input Actions")]
    [SerializeField]
    InputActionReference buildInput;

    [SerializeField]
    InputActionReference sellBuildingInput;

    [SerializeField]
    InputActionReference meleeSwitch;

    [Header("Melee Attack")]
    [SerializeField]
    float meleeRange;

    [SerializeField]
    float meleeRadius;

    [SerializeField]
    float meleeLookSpeed;

    [SerializeField]
    float meleeCD;

    float meleeTimer;

    //Offsets the attack position from the lookDir
    [SerializeField]
    float attackOffset;

    [SerializeField]
    int meleeDamage;

    [SerializeField]
    float knockbackForce;

    [SerializeField]
    public bool isMeleeMode = false; // Made public for UIControls system

    [SerializeField]
    LayerMask enemyLayer;

    [SerializeField]
    GameObject attackEffect;

    [Header("Sound")]
    [SerializeField]
    string buildModeSFX;

    [SerializeField]
    string meleeModeSFX;

    [SerializeField]
    string buildSFX;

    [SerializeField]
    string sellSFX;

    Node oldSelect;

    private void Start()
    {
        /*Singleton logic
        if (instance == null) {
            instance = this;
        } else {
            Destroy(gameObject);
        }*/

        lsc = GetComponent<LocalSoundComposer>();

        cam = Camera.main;
        pillowText.text = pillows.ToString();

        PathfindingGrid.instance.buildPlacement = buildIndicator;

        pi = GetComponent<PlayerInput>();
    }

    private void Update()
    {
        if (isMeleeMode) {
            meleeTimer += Time.deltaTime;
        }
    }

    public void takeInput(Vector2 cursor, string currentControlScheme)
    {
         if (RemoraDevConsole.GameplayInputBlocked()) { return; }

        if (currentControlScheme == "MnK") {
            Vector2 mousePos = lookInput.action.ReadValue<Vector2>();
            Vector2 worldMousePos = cam.ScreenToWorldPoint(mousePos);

            lookDir = worldMousePos - (Vector2)transform.position;
            if (isMeleeMode) {
                /*if (lookDir.magnitude > meleeRange) {*/
                    lookDir.Normalize();
                    lookDir *= meleeRange;
                //}
            } else  if(lookDir.magnitude > buildRange) {
                lookDir.Normalize();
                lookDir *= buildRange;
            }
        } else if(currentControlScheme == "Gamepad") {
            Vector2 joystickInput = lookInput.action.ReadValue<Vector2>();
            //Settings.rawJoystickInputf
            if (isMeleeMode) {
                lookDir = joystickInput * meleeRange;
            } else {
                lookDir += joystickInput * Time.deltaTime * Settings.joystickLookSensitivity;
                if (lookDir.magnitude > buildRange) {
                    lookDir.Normalize();
                    lookDir *= buildRange;
                }
            }
        }



        if(isMeleeMode) {
            if(lookDir.magnitude != 0) {
                lastLookDir = lookDir;
            }
            float angle = Mathf.Atan2(lastLookDir.y, lastLookDir.x) * Mathf.Rad2Deg;

            Vector2 attackDir = attackIndicator.position - transform.position;
            float attackAngle = Mathf.Atan2(attackDir.y, attackDir.x) * Mathf.Rad2Deg;

            float newAngle = Mathf.LerpAngle(attackAngle, angle, meleeLookSpeed * Time.deltaTime);

            Vector2 newDir = new Vector2(Mathf.Cos(newAngle * Mathf.Deg2Rad), Mathf.Sin(newAngle * Mathf.Deg2Rad));

            //Debug.Log($"Angle: {angle}, newAngle: {newAngle}, newDir: {newDir}");

            attackIndicator.position = (Vector2)transform.position + newDir * meleeRange;
            
            if(oldSelect != null) {
                //Fucks garbage collection if not saving in a variable
                BuildingHealth oldHP = oldSelect.building.GetComponent<BuildingHealth>();
                oldHP.SetRangeVisual(false);
                oldSelect = null;
            }
        } else {
            buildPos = (Vector2)transform.position + lookDir;
            buildIndicator.position = buildPos;
            Node node = PathfindingGrid.instance.NodeFromWorldPoint(buildPos);

            if(oldSelect != null )

            if(oldSelect != null && oldSelect.building != null) {
                BuildingHealth oldHP = oldSelect.building.GetComponent<BuildingHealth>();
                oldHP.SetRangeVisual(false);
            }


            Node playerNode = PathfindingGrid.instance.NodeFromWorldPoint((Vector2)transform.position);
            if(node.building != null) {
                buildingIndicator.position = node.worldPosition;
                node.building.GetComponent<BuildingHealth>().SetRangeVisual(true);
                oldSelect = node;

                //If the hovered node is the same node as the node the player is standing on, dont show the preview
                if(playerNode != node) {
                    buildingIndicator.gameObject.SetActive(true);
                } else {
                    buildingIndicator.gameObject.SetActive(false);
                }
                placementIndicator.gameObject.SetActive(false);
            } else {
                placementIndicator.position = node.worldPosition;
                buildingIndicator.gameObject.SetActive(false);

                //See comment earlier
                if (playerNode != node) {
                    placementIndicator.gameObject.SetActive(true);
                } else {
                    placementIndicator.gameObject.SetActive(false);
                }
            }
        }

    }

    public BuildingScriptableObject GetSelectedBuilding() {
        return buildings[selectedBuilding];
    }

    public void changePillowAmount(int amount)
    {
        pillows += amount;
        pillowText.text = pillows.ToString();
    }

    public int getPillows() {
        return pillows;
    }

    public void setPillows(int amount) {
        pillows = amount;
        pillowText.text = pillows.ToString();
    }

    public void SwitchBuildSelection(int selection)
    {
        if(selection >= buildings.Length)
        {
            return;
        }
        UIHandler.Instance.highlightBuildingSelected();
        selectedBuilding = selection;
    }

    public void playerUseFunc() {
        if (isMeleeMode) {
            meleeAttack();
        } else {
            placeBuilding();
        }
    }

    //Playernode and placement node check is done in the grid logic
    private void placeBuilding() {
        if (RemoraDevConsole.GameplayInputBlocked()) { return; }
        int buildingCost = (int)Math.Round(
            buildings[selectedBuilding].buildingCost * GlobalBuyInflationMultiplier,
            MidpointRounding.AwayFromZero
        );

        if (pillows < buildingCost) {
            Debug.Log("Not enough pillows");
            return;
        }

        bool placed = PathfindingGrid.instance.CreateBuilding(buildings[selectedBuilding].buildingPrefab);

        if (placed) {
            lsc.PlayFx(buildSFX);
            changePillowAmount(-buildingCost);
            return;
        }
        Debug.Log("Cannot place building here");
    }

    private void meleeAttack() {
        if (meleeTimer < meleeCD) {
            return;
        }
        meleeTimer = 0;

        Vector2 attackPos = (Vector2)attackIndicator.position - (attackOffset * lookDir.normalized);
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(attackPos, meleeRadius, enemyLayer);

        foreach (Collider2D hitCollider in hitColliders) {
            EnemyHealth eh = hitCollider.GetComponent<EnemyHealth>();
            if (eh == null) {
                continue;
            }
            eh.TakeDamage(meleeDamage);

            Rigidbody2D rb = hitCollider.GetComponent<Rigidbody2D>();
            if (rb != null) {
                Vector2 knockbackDir = (hitCollider.transform.position - transform.position).normalized;
                rb.AddForce(knockbackDir * knockbackForce, ForceMode2D.Impulse);
            }
        }

        GameObject slashEffect = Instantiate(attackEffect, attackPos, Quaternion.identity);
        slashEffect.transform.right = lookDir;
    }
    public void removeBuilding(InputAction.CallbackContext obj) {
        if (RemoraDevConsole.GameplayInputBlocked()) { return; }

        int? sellAmount = PathfindingGrid.instance.RemoveBuilding(buildPos);
        if(sellAmount != null) {
            lsc.PlayFx(sellSFX);
            //Debug.Log("Sold building for " + sellAmount + " pillows");
            //MARK: Simplify casting?
            changePillowAmount(
                (int)Math.Round(
                    (int)sellAmount * GlobalSellInflationMultiplier,
                    MidpointRounding.AwayFromZero
                )
            );
            return;
        }
        //Debug.Log("No building to sell here");
    }

    public void ForcePlaceBuildingAt(int NodeX, int NodeY) {
        bool placed = PathfindingGrid.instance.ForceBuildingAtNode(buildings[selectedBuilding].buildingPrefab, NodeX, NodeY);
        if (placed) {
            lsc.PlayFx(buildSFX);
            pillows -= (int)Math.Round(
                buildings[selectedBuilding].buildingCost * GlobalBuyInflationMultiplier,
                MidpointRounding.AwayFromZero
            );
            pillowText.text = pillows.ToString();
        }
    }


    public void switchMeleeMode(bool newMeleeMode) {
        isMeleeMode = newMeleeMode;
        if (!isMeleeMode) {
            lsc.PlayFx(buildModeSFX);

            attackIndicator.gameObject.SetActive(false);
            buildIndicator.gameObject.SetActive(true);
            buildingIndicator.gameObject.SetActive(true);
            placementIndicator.gameObject.SetActive(true);
        } else {
            lsc.PlayFx(meleeModeSFX);

            attackIndicator.gameObject.SetActive(true);
            buildIndicator.gameObject.SetActive(false);
            buildingIndicator.gameObject.SetActive(false);
            placementIndicator.gameObject.SetActive(false);
        }

        // Update mainTokenIconResolver's text
        if (mainGameTokenIconResolver) {
            mainGameTokenIconResolver.UpdateMeleeMode(isMeleeMode);
        }
    }

    public void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, meleeRange);

        //Added unnecessary logic to mimic attack logic
        Gizmos.DrawWireSphere((Vector2)transform.position + lookDir - (attackOffset * lookDir.normalized), meleeRadius);
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, buildRange);
    }
}

