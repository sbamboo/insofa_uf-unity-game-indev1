using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class InputHandler : MonoBehaviour {
    private static InputHandler instance;
    public static InputHandler Instance { get { return instance; } }

    //References
    [SerializeField]
    EventSystem eventSystem;

    [SerializeField]
    PlayerHand playerHand;

    [SerializeField]
    PlayerController playerController;

    [Header("Input actions")]
    [SerializeField]
    private InputActionReference playerUse;

    [SerializeField]
    private InputActionReference playerSecondaryUse;

    [SerializeField]
    private InputActionReference playerMove;

    //Could be both the joystick and mouse
    [SerializeField]
    private InputActionReference playerCursorInput;

    [SerializeField]
    private InputActionReference playerMeleeSwitch;


    #region Private Variables
    private bool isHoveringButton = false;

    private bool playing = true;
    #endregion

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start() {
        if (instance == null) {
            instance = this;
        } else {
            Destroy(gameObject);
        }

        eventSystem.IsPointerOverGameObject();

        playerUse.action.started += PlayerUseHandler;
        playerSecondaryUse.action.started += playerHand.removeBuilding;
        playerMeleeSwitch.action.started += SwitchMeleeModeHandler;
    }

    //Should be called after input is taken, hopefully (logic to avoid double inputs are here)
    private void Update() {
        if (RemoraDevConsole.GameplayInputBlocked()) {
            playerController.takeInput(Vector2.zero);
            playing = false;
            return;
        }

        playing = true;

        playerController.takeInput(playerMove.action.ReadValue<Vector2>());
        playerHand.takeInput(playerCursorInput.action.ReadValue<Vector2>(), PlatformInputHandler.Instance.currentInputScheme);

        isHoveringButton = eventSystem.IsPointerOverGameObject();
    }

    private void PlayerUseHandler(InputAction.CallbackContext obj) {
        if (RemoraDevConsole.GameplayInputBlocked()) {
            playerController.takeInput(Vector2.zero);
            playing = false;
            return;
        }

        if (isHoveringButton && PlatformInputHandler.Instance.currentInputScheme == PlatformInputHandler.SCHEME_MnK && playing){
            return;
        }
        playerHand.playerUseFunc();
    }

    private void SwitchMeleeModeHandler(InputAction.CallbackContext obj) {
        if (RemoraDevConsole.GameplayInputBlocked()) { return; }

        bool newIsMeleeMode = !playerHand.isMeleeMode;
        if (newIsMeleeMode) {
            playerSecondaryUse.action.started -= playerHand.removeBuilding;
        } else {
            playerSecondaryUse.action.started += playerHand.removeBuilding;
        }

        playerHand.switchMeleeMode(newIsMeleeMode);
    }

    //This section would be usefull if we decide to centralize the buttons through this sole script
    #region to be removed?
    /*
    public void buttonClick(string buttonInfo) {

        string[] strings = buttonInfo.Split(" ");
        if (strings.Length <= 0) {
            Debug.LogWarning($"Button info not formatted properly: {buttonInfo}");
            return;
        }


        Debug.Log(strings[0]);


        bool success = false;
        if (strings.Length > 1) {
            success = buttonWithMessage(strings[0], strings[1]);
        } else {
            success = buttonWithoutMessage(strings[0]);
        }

        Debug.Log($"{success} : {strings[1]}");

        if (!success) {
            Debug.LogError($"Failed to execute button logic: {strings}");
        }
    }
    private bool buttonWithoutMessage(string buttonAction) {
        return false;
    }

    private bool buttonWithMessage(string buttonAction, string buttonMessage) {
        switch (buttonAction) {
            case "SwitchSelection":
                int selection;
                try {
                    selection = int.Parse(buttonMessage);
                } catch {
                    return false;
                }
                PlayerHand.Instance.SwitchBuildSelection(selection);
                return true;
        }
        return false;
    }
    */
    #endregion
}
