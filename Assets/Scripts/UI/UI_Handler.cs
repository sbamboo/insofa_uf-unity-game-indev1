using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


public class UIHandler : MonoBehaviour
{
    private static UIHandler instance;
    public static UIHandler Instance {  get { return instance; } }


    EventSystem eventSystem;

    [SerializeField]
    public bool demoMode = false;

    // Reference to mainGameTokenIconResolver
    [SerializeField]
    public MainGame_TokenIconResolver mainGameTokenIconResolver;
    
    [SerializeField]
    public GameObject debugOverlay;

    [SerializeField]
    public InputActionReference debugButton;

    [SerializeField]
    public InputActionReference menuButton;

    [SerializeField]
    public InputActionReference quitButton;

    [SerializeField]
    public GameObject quitButtonObj;

    private int currentScene;

    [Header("Menus")]

    [SerializeField]
    private GameObject pauseMenu;

    [SerializeField]
    private GameObject optionsMenu;

    [SerializeField]
    private GameObject turretMenu;

    [SerializeField]
    private GameObject wallMenu;

    [Header("Interactable Objects")]
    [SerializeField]
    GameObject[] pauseMenuInteractableObjects;

    [SerializeField]
    GameObject[] optionsMenuInteractableObjects;

    [SerializeField]
    GameObject[] mainInteractableObjects;

    [SerializeField]
    GameObject[] turretSelectionInteractableObjects;

    [SerializeField]
    GameObject[] wallSelectionInteractableObjects;

    //0 = Gameplay/Main Menu, 1 = Pause Menu, 2 = Options Menu
    public int currentMenu = 0;                                    // Made public for UIControls system


    private void Start() {
        if (instance == null) {
            instance = this;
        } else {
            Destroy(this);
        }


        currentScene = SceneManager.GetActiveScene().buildIndex;

        eventSystem = EventSystem.current;

        if (demoMode == true) {
            // DeActivate the quit button
            if (quitButtonObj != null) {
                quitButtonObj.SetActive(false);
            }
        }
    }

    private void Update()
    {   
        /*
        if (Input.GetKeyUp(KeyCode.Q))
        {
            Debug.Log("switched");
            healthSlider.gameObject.SetActive(!healthSlider.gameObject.activeSelf);
            healthSlider2.gameObject.SetActive(!healthSlider2.gameObject.activeSelf);
        }
        */

        if (menuButton != null) {
            menuButton.action.started += togglePauseMenu;
        }

        if (debugButton != null)
        {
            debugButton.action.started += toggleDebugOverlay;
        }

        if (quitButton != null) {
            quitButton.action.started += quitGame;
        }
    }

    public void loadScene(int value) {
        //To make sure the game is unpaused when loading a new scene
        Time.timeScale = 1;

        SceneManager.LoadScene(value);
    }

    public void togglePauseMenu(InputAction.CallbackContext obj) {
        if (currentMenu == 1) {
            switchMenu(0);
            return;
        }

        switchMenu(1);
    }

    //if newMenu = curentMenu, close menu and return to menu 0, else open newMenu
    public void switchMenu(int newMenu) {
        // Update mainTokenIconResolver's text
        if (mainGameTokenIconResolver) {
            mainGameTokenIconResolver.UpdateCurrentMenu(newMenu);
        }

        if (currentMenu == newMenu || newMenu == 0) {
            currentMenu = 0;
            updateMenu(null, mainInteractableObjects);

            //Unpauses the game
            Time.timeScale = 1;

            return;
        }

        //Pauses the game
        Time.timeScale = 0;

        switch (newMenu) {
            case 1:
                if (currentScene == 1) {
                    updateMenu(pauseMenu, pauseMenuInteractableObjects);
                }
                break;
            case 2:
                updateMenu(optionsMenu, optionsMenuInteractableObjects);
                break;
        }
        currentMenu = newMenu;
    }

    private void updateMenu(GameObject menuToShow, GameObject[] interactiveList) {
        if(pauseMenu != null) pauseMenu.SetActive(false);
        if (optionsMenu != null) optionsMenu.SetActive(false);

        // Enable/Disable Interactable Objects to make sure they dont interfere with UI navigation
        for (int i = 0; i < pauseMenuInteractableObjects.Length; i++) {
            if(pauseMenuInteractableObjects[i] == null) {
                Debug.LogError("pauseMenuInteractableObjects[" + i + "] is null");
                continue;
            }

            Selectable selectable = pauseMenuInteractableObjects[i].GetComponent<Selectable>();
            if (selectable != null) {
                selectable.interactable = false;
            }
        }

        for (int i = 0; i < optionsMenuInteractableObjects.Length; i++) {
            if(optionsMenuInteractableObjects[i] == null) {
                Debug.LogError("optionsMenuInteractableObjects[" + i + "] is null");
                continue;
            }

            Selectable selectable = optionsMenuInteractableObjects[i].GetComponent<Selectable>();
            if (selectable != null) {
                selectable.interactable = false;
            }
        }

        for (int i = 0; i < mainInteractableObjects.Length; i++) {
            if(mainInteractableObjects[i] == null) {
                Debug.LogError("mainInteractableObjects[" + i + "] is null");
                continue;
            }

            Selectable selectable = mainInteractableObjects[i].GetComponent<Selectable>();
            if (selectable != null) {
                selectable.interactable = false;
            }
        }

        if(menuToShow != null) menuToShow.SetActive(true);

        // Enable/Disable Interactable Objects to make sure they dont interfere with UI navigation
        for (int i = 0; i < interactiveList.Length; i++) {
            Selectable selectable = interactiveList[i].GetComponent<Selectable>();
            if (selectable != null) {
                selectable.interactable = true;
                if (i == 0) {
                    selectable.Select();
                }
            }
        }
    }
    public void expandMenu(int menu) {
        switch (menu) {
            case 1:
                expandMenu(turretMenu, turretSelectionInteractableObjects);
                break;
            case 2:
                expandMenu(wallMenu, wallSelectionInteractableObjects);
                break;
        }
    }

    public void expandMenu(GameObject menuToToggle, GameObject[] interactiveList) {
        menuToToggle.SetActive(!menuToToggle.activeSelf);
        for (int i = 0; i < interactiveList.Length; i++) {
            Button button = interactiveList[i].GetComponent<Button>();
            if (button != null) {
                button.interactable = menuToToggle.activeSelf;
                continue;
            }
            Slider slider = interactiveList[i].GetComponent<Slider>();
            if (slider != null) {
                slider.interactable = menuToToggle.activeSelf;
            }
        }
    }

    
    public void highlightBuildingSelected() {
        GameObject gameObject = eventSystem.currentSelectedGameObject;
        //Debug.Log(gameObject.name);

        List<GameObject> buildingButtons = new List<GameObject>();
        buildingButtons.AddRange(turretSelectionInteractableObjects);
        buildingButtons.AddRange(wallSelectionInteractableObjects);

        //Disable child highlighter in all building buttons
        for (int i = 0; i < buildingButtons.Count; i++) {
            //Debug.Log(buildingButtons[i].name);
            GameObject child = buildingButtons[i].transform.GetChild(0).gameObject;
            child.SetActive(false);
        }

        gameObject.transform.GetChild(0).gameObject.SetActive(true);
    }

    public void QuitGame() {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false; // Stop play mode in Unity Editor
#else
        Application.Quit(); // Quit standalone build
#endif
    }

    public void quitGame(InputAction.CallbackContext obj) {
        QuitGame();
    }

    public void toggleDebugOverlay(InputAction.CallbackContext obj)
    {
        if (currentScene == 1)
        {
            if (RemoraDevConsole.GameplayInputBlocked()) { return; }
            debugOverlay.SetActive(!debugOverlay.activeSelf);
        }

        /*
        Button button = interactiveList[i].GetComponent<Button>();
        if (button != null) {
        button.interactable = menuToToggle.activeSelf;
        continue;
        }

        Slider slider = interactiveList[i].GetComponent<Slider>();
        if (slider != null) {
        slider.interactable = menuToToggle.activeSelf;
        }
        */
    }
}
