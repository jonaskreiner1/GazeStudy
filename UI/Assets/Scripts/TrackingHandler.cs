using UnityEngine;
using System.IO;
using System;
using System.Collections;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using UnityEditor;
using System.Globalization;

public class TrackingHandler : MonoBehaviour
{

    // Access during Runtime

    public int StudyID = 0;
    public int SelectionMode = 1;
    public float studyStartTimer = 0f;  // Timer variable
    public float studyStartTime = 30f;  // Timer duration to start the study (in seconds)
    public int currentIndex = 0;

    // Placement of Canvas relative to Camera
    public GameObject referencedCamera;  // The camera representing the user's gaze /CenterEyeAnchor  public GameObject canvas;
    public GameObject canvas;            // The Canvas that follows the camera
    public GameObject cursor;            // The cursor that should follow the gaze direction
    public GameObject collisionSelectionButtonObject;
    public GameObject centerMarkCanvas;
    public GameObject centerMarkGlobal;
    public float zOffsetCanvasToCamera = 10f; // Camera Offset
    private bool UIActive;

    //Head Gaze
    public Transform headTransform; // Assign this to the OVRCameraRig's centerEyeAnchor tio retrive Head Gaze
    // Nodding
    private bool nodInProgress = false;
    private float nodThresholdDown = 10f;  // How much the user must tilt down (in degrees)
    private float nodThresholdUp = 50f;    // How much the user must return up (in degrees)
    private float lastHeadPitch = 0f;
    private float minTimeBetweenNods = 0.5f; // Prevents multiple nods in a short time
    private float lastNodTime = 0f;

    // EyeGaze
    public OVREyeGaze eyeGaze; // Assign Script where EyeGaze is attached to this
    public float smoothing = 10f; // Smoothing for Eyegaze
    public float gazeXOffset = 0;
    public float gazeYOffset = 0;
    public float scaleX = 1;
    public float scaleY = 1;
    public float timeOnTarget = 0;

    public bool eyeTrackingEnabled = true;
    // Calibration Eye Gaze
    public GameObject CalibrationParent; // Reference to the parent object
    private int calibrationIndex = 0;
    private Transform[] children;
    private bool isCalibrating = false;
    private Vector2[] rawGazeData = new Vector2[9];   // Stores uncalibrated gaze points
    private Vector2[] targetPoints = new Vector2[9];  // Stores known screen positions
    //Blinking
    private OVRFaceExpressions faceComponent; // Reference to the OVRFaceExpressions component
    private float leftEyeBlinkWeight = -1.0f;
    private float rightEyeBlinkWeight = -1.0f;
    private bool isBlinking = false;
    public float blinkThreshold = 0.1f;
    private float blinkDuration = 0.15f; // How long the eyes need to stay closed for it to be considered a blink
    private float blinkTimer = 0f;
    // Winking
    private float winkStartTime = -1f;
    private const float winkClosedThreshold = 0.05f;
    private const float winkTimeThreshold = 0.05f; // 100 ms in seconds
    
    private bool isWinking = false;

    // Arrow Indicating Button
    public GameObject buttonIndicator; // The Renderer of the target GameObject (button indicator)
    public Transform buttonTransform; // The Transform of the button

    // Timer, Filepath
    string filePath;
    private float startTime;
    private bool isTiming = false;


    // Study process

    public List<int> targetPool = new List<int>();

    public int targetButton = 1;
    public bool studyStarted = false;
    public float nextButtonTimer = 0f;  // Timer variable
    private int studyStartSequence = 0;
    public AudioClip studyStartSound; // The audio clip to play when the study starts
    private AudioSource audioSource; // Reference to the AudioSource component
    public int FalsePositives = 0;

    void Start()
    {




        ToggleCentroids();
        // Make sure Arrow is off at beginning
        buttonIndicator.SetActive(false);

        // Generate 
        InitializeOrderOfUIButtons();

        CheckIfFaceTrackingIsActive();

        CalibrateEyeGaze();


        // Add Audio Source
        audioSource = gameObject.AddComponent<AudioSource>();

    }

    void Update()
    {
        AlignCanvasWithCamera();

        //During Calibration
        if (isCalibrating)
        {
            if (Input.GetKeyDown(KeyCode.Space)) RecordCalibrationPoint();
        }

        //Outside Calibration
        else
        {
            nextButtonTimer += Time.deltaTime;
            studyStartMethod();


            // Screen off: Show Target
            if (targetButton == 0 && nextButtonTimer >= 2)
            {
                if (studyStarted) targetButton = PickTarget(); // Chooses Randomized Target for Study 
                else targetButton = UnityEngine.Random.Range(1, 5);    // Chooses target for training
                IndicateButton();
            }

            if (Input.GetKeyDown(KeyCode.Space) && targetButton != 0)
            {
                HideArrow();
                ToggleCanvas(true);
                StartTimer(true, "");
            }


            // Move Cursor, Check for Selections when UI Active
            if (SelectionMode == 1) { AlignCursorEyeGaze(); }   // Extra Button (Inside CollisionSelection)
            else if (SelectionMode == 2) { AlignCursorEyeGaze(); } // Dwell Time (Inside Collision UI)
            else if (SelectionMode == 3) { AlignCursorEyeGaze(); DetectWink(); }
            else if (SelectionMode == 4) { AlignCursorEyeGaze(); DetectBlink(); }
            else if (SelectionMode == 5) { AlignCursorEyeGaze(); DetectNod(); }
            else if (SelectionMode == 6) { AlignCursorHeadGaze(); } // Extra Button (Inside CollisionSelection)
        }
    }


    // Align Canvas (always active)
    void AlignCanvasWithCamera()
    {
        Vector3 canvasPosition = referencedCamera.transform.position + referencedCamera.transform.forward * zOffsetCanvasToCamera;
        canvasPosition.y -= 350f; // Move canvas down by 121 units
        canvas.transform.position = canvasPosition;
        canvas.transform.rotation = referencedCamera.transform.rotation;
    }

    // Cursor Positioning
    void AlignCursorHeadGaze()
    {
        // Get camera rotation angles (Pitch = X-axis, Yaw = Y-axis)
        float cameraPitch = referencedCamera.transform.eulerAngles.x; // Up/Down rotation
        float cameraYaw = referencedCamera.transform.eulerAngles.y;   // Left/Right rotation

        // Convert Euler angles to a range of -180° to 180° to prevent wrapping issues
        if (cameraPitch > 180f) cameraPitch -= 360f;
        if (cameraYaw > 180f) cameraYaw -= 360f;

        // Normalize rotation values to a range (-1 to 1)
        float normalizedPitch = Mathf.Clamp(cameraPitch / 30f, -1f, 1f); // -90° to 90° mapped to -1 to 1
        float normalizedYaw = Mathf.Clamp(cameraYaw / 53f, -1f, 1f);     // -90° to 90° mapped to -1 to 1

        // Define the range for cursor movement within the canvas
        float maxX = 1920f; // Half of 1920px
        float maxY = 1080f; // Half of 1080px

        // Compute cursor position based on camera rotation
        float cursorX = normalizedYaw * maxX;
        float cursorY = -normalizedPitch * maxY;//-121f; // Inverted Y to match natural movement

        // Set the cursor position relative to the canvas
        cursor.transform.localPosition = new Vector3(cursorX, cursorY, +3);
    }

    void AlignCursorEyeGaze()
    {

        if (eyeGaze == null || headTransform == null || cursor == null)
        {
            Debug.LogError("Missing references in EyeGazeCursorWithHeadAlignment script!");
            return;
        }

        if (OVRPlugin.eyeTrackingEnabled && eyeGaze.gameObject.activeInHierarchy)
        {
            // Get the raw gaze direction and apply offset correction
            Vector3 gazeDirection = eyeGaze.transform.forward;

            // Adjust gaze direction based on head rotation
            gazeDirection = headTransform.rotation * gazeDirection;

            // Project gaze onto a plane in front of the camera
            Vector3 targetPosition = headTransform.position + (gazeDirection * (zOffsetCanvasToCamera));

            // Convert to local space relative to the head
            Vector3 localTargetPosition = headTransform.InverseTransformPoint(targetPosition);

            // Apply offset correction to counteract drift
            localTargetPosition.x = (localTargetPosition.x + gazeXOffset) * scaleX;
            localTargetPosition.y = (localTargetPosition.y + gazeYOffset) * scaleY;
            localTargetPosition.y -= 350f;

            // Maintain depth consistency
            localTargetPosition.z = 2300;

            // Convert back to world space
            Vector3 fixedWorldPosition = headTransform.TransformPoint(localTargetPosition);

            // Smooth cursor movement
            cursor.transform.position = Vector3.Lerp(cursor.transform.position, fixedWorldPosition, Time.deltaTime * smoothing);
        }
    }

    void DetectBlink()
    {
        if (faceComponent)
        {
            // Get the weights for the left and right eye closed expressions
            faceComponent.TryGetFaceExpressionWeight(OVRFaceExpressions.FaceExpression.EyesClosedL, out leftEyeBlinkWeight);
            faceComponent.TryGetFaceExpressionWeight(OVRFaceExpressions.FaceExpression.EyesClosedR, out rightEyeBlinkWeight);

            // Detect if both eyes are closed (eyes closed when weight is below threshold)
            bool isLeftEyeBlinking= leftEyeBlinkWeight > blinkThreshold;
            bool isRightEyeBlinking = rightEyeBlinkWeight > blinkThreshold;

            // If both eyes are closed
            if (isLeftEyeBlinking && isRightEyeBlinking)
            {
                blinkTimer += Time.deltaTime;

                // Consider it a blink if both eyes stay closed for the required duration
                if (blinkTimer >= blinkDuration)
                {
                    if (!isBlinking)
                    {
                        isBlinking = true;
                        if (CollisionUIButton.currentlyHighlighted != null) CollisionUIButton.currentlyHighlighted.ButtonSelected();
                        else if (studyStarted && isTiming) FalsePositives++;

                    }
                }
            }
            else
            {
                blinkTimer = 0f;

                // Reset if blink has ended
                if (isBlinking)
                {
                    isBlinking = false;
                    Debug.Log("Blink Ended!");
                }
            }
        }

    }

    void DetectWink()
    {



        if (faceComponent)
        {
            // Get the weights for the left and right eye closed expressions
            faceComponent.TryGetFaceExpressionWeight(OVRFaceExpressions.FaceExpression.EyesClosedL, out leftEyeBlinkWeight);
            faceComponent.TryGetFaceExpressionWeight(OVRFaceExpressions.FaceExpression.EyesClosedR, out rightEyeBlinkWeight);

            // Detect if the eyes are closed (eyes closed when weight is below threshold)
            bool isLeftEyeBlinking = leftEyeBlinkWeight < winkClosedThreshold;
            bool isRightEyeBlinking = rightEyeBlinkWeight < winkClosedThreshold;

            // Check if either the left or right eye is closed, and the other is open
            if (isLeftEyeBlinking != isRightEyeBlinking)
            {
                if (winkStartTime < 0f)
                {
                    // Start tracking the wink
                    winkStartTime = Time.time;
                }
                else if (Time.time - winkStartTime >= winkTimeThreshold)
                {
                    // Wink detected if one eye remains closed for at least 100 ms
                    if (!isWinking)
                    {
                        isWinking = true;
                        if (CollisionUIButton.currentlyHighlighted != null) CollisionUIButton.currentlyHighlighted.ButtonSelected();
                        else if (studyStarted && isTiming) FalsePositives++;

                    }
                }
            }
            else
            {
                // Reset if neither or both eyes are closed
                winkStartTime = -1f;
                isWinking = false;
            }
        }

    }

    void DetectNod()
    {


        if (headTransform == null)
        {
            Debug.LogError("Head transform is not assigned");
            return;
        }

        float currentPitch = headTransform.eulerAngles.x;

        // Convert to -180 to 180 range
        if (currentPitch > 180f) currentPitch -= 360f;

        // Detect downward movement
        if (!nodInProgress && currentPitch > nodThresholdDown)
        {
            nodInProgress = true;
        }

        // Detect upward movement
        if (nodInProgress && currentPitch < nodThresholdUp)
        {
            if (Time.time - lastNodTime > minTimeBetweenNods)
            {
                lastNodTime = Time.time;
                if (CollisionUIButton.currentlyHighlighted != null) CollisionUIButton.currentlyHighlighted.ButtonSelected();
                else if (studyStarted) FalsePositives++;
            }
            nodInProgress = false;
        }

        lastHeadPitch = currentPitch;

    }

    public void ToggleCanvas(bool isActive)
    {
        int uiLayer = LayerMask.NameToLayer("UIVisible"); // Get the layer index

        if (uiLayer == -1)
        {
            Debug.LogError("Layer 'UIVisible' not found. Make sure it exists in the project settings.");
            return;
        }

        GameObject[] uiElements = FindObjectsOfType<GameObject>(true); // Find ALL objects, even inactive ones

        foreach (GameObject obj in uiElements)
        {
            if (obj.layer == uiLayer) // Check if object is on the "UIVisible" layer
            {
                if (isActive)
                {
                    Transform parent = obj.transform.parent;
                    while (parent != null)
                    {
                        parent.gameObject.SetActive(true); // Ensure parent is active
                        parent = parent.parent; // Move up the hierarchy
                    }
                }

                obj.SetActive(isActive); // Finally, activate/deactivate the object itself
                centerMarkCanvas.SetActive(!isActive);
                centerMarkGlobal.SetActive(!isActive);
                UIActive = isActive;
            }
        }

    }

    public void StartTimer(bool status, string buttonName)
    {

        if (buttonName == "ToT")
        {
            timeOnTarget = Time.time - startTime; // Calculate elapsed time
        }
        else if (status)
        {
            cursor.transform.localPosition = Vector3.zero; // Center the cursor
            CollisionUIButton.currentlyHighlighted = null;
            startTime = Time.time; // Store the start time
            isTiming = true;
            Debug.Log("Timer started.");
        }
        else
        {
            if (isTiming)
            {
                float elapsedTime = Time.time - startTime; // Calculate elapsed time

                // Write to CSV file
                SaveCSV(timeOnTarget, elapsedTime, SelectionMode, buttonName, targetButton);
                targetButton = 0;
                nextButtonTimer = 0;
            }
            else
            {
                Debug.LogWarning("Timer was not running.");
            }
        }
    }

    public void SaveCSV(float timeOnTarget, float duration, int SelectionMode, string buttonName, int targetButton)
    {
        if (!studyStarted) return;
        // Get the current timestamp
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        bool correctSelection = buttonName == targetButton.ToString();
        float selectionTime = duration-timeOnTarget;

        // Create the data line with timestamp
        string dataLine = $"{SelectionMode};{timestamp};{timeOnTarget.ToString(CultureInfo.InvariantCulture)};{duration.ToString(CultureInfo.InvariantCulture)};{selectionTime.ToString(CultureInfo.InvariantCulture)};{buttonName};{targetButton};{correctSelection}\n";

        try
        {
            // Append the data line with the timestamp to the file
            File.AppendAllText(filePath, dataLine);
            Debug.Log("Data saved: " + dataLine);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error saving tracking data: " + e.Message);
        }
    }

    public void SaveCSVHeader(string msg)
    {
        try
        {
            // Get the current timestamp
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Append the message with the timestamp
            string messageWithTimestamp = $"{msg};{timestamp}\n";

            // Save the message with the timestamp to the file
            File.AppendAllText(filePath, messageWithTimestamp);
            if (msg == "UIStudyStarted") File.AppendAllText(filePath, "selectionMode;timestamp;timeOnTarget;duration;selectionTime;selectedButton;targetButton;correctSelection;\n");


        }
        catch (System.Exception e)
        {
            Debug.LogError("Error saving tracking data: " + e.Message);
        }
    }

    void CalibrateEyeGaze()
    {
        // Get all children of the specified parent object
        children = new Transform[CalibrationParent.transform.childCount];
        for (int i = 0; i < CalibrationParent.transform.childCount; i++)
        {
            children[i] = CalibrationParent.transform.GetChild(i);
            children[i].gameObject.SetActive(false); // Deactivate all initially
        }
        isCalibrating = true;
        ActivateNextCalibrationPoint();
    }

    public int PickTarget()
    {
        if (currentIndex >= targetPool.Count)
        {
            SaveCSVHeader("FalsePostives;" + FalsePositives);
            SaveCSVHeader("StudyEnd");
            Debug.LogError("All 20 targets have been picked. Study Over!");
        }
        int target = targetPool[currentIndex];
        currentIndex++;
        return target;
    }

    public void IndicateButton()
    {
        buttonIndicator.SetActive(true);
        switch (targetButton)
        {
            case 1: // Up
                    // Rotate around the X-axis with the normal position for Left as a base
                buttonTransform.localRotation = Quaternion.Euler(90 + 90, 90, -90); // Rotate around the X-axis by 90 degrees
                break;

            case 2: // Down
                    // Rotate around the X-axis with the normal position for Left as a base
                buttonTransform.localRotation = Quaternion.Euler(90 - 90, 90, -90); // Rotate around the X-axis by -90 degrees
                break;

            case 3: // Left (natural position)
                    // No rotation applied, just the default position (x=90, y=90, z=-90)
                buttonTransform.localRotation = Quaternion.Euler(90, 90, -90);
                break;

            case 4: // Right
                    // Rotate around the X-axis with the normal position for Left as a base
                buttonTransform.localRotation = Quaternion.Euler(270, 90, -90); // Adjust to rotate right (x=90, y=90, z=90)
                break;

            default:
                return;
        }
    }

    private void InitializeOrderOfUIButtons()
    {
        targetPool.Clear();
        for (int i = 1; i <= 4; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                targetPool.Add(i);
            }
        }

        // Shuffle using Fisher-Yates
        for (int i = targetPool.Count - 1; i > 0; i--)
        {
            int randIndex = UnityEngine.Random.Range(0, i + 1);
            (targetPool[i], targetPool[randIndex]) = (targetPool[randIndex], targetPool[i]);
        }

        currentIndex = 0;
    }

    void ActivateNextCalibrationPoint()
    {
        if (calibrationIndex > 0 && calibrationIndex <= children.Length)
        {
            children[calibrationIndex - 1].gameObject.SetActive(false); // Deactivate previous child
        }
        if (calibrationIndex < children.Length)
        {
            children[calibrationIndex].gameObject.SetActive(true);

            // Add child position to coordinate sum
            Vector3 position = children[calibrationIndex].localPosition;
            targetPoints[calibrationIndex] = position;
        }
        else
        {
            isCalibrating = false;
            ComputeGazeCalibration();
        }
    }

    void RecordCalibrationPoint()
    {
        if (isCalibrating && calibrationIndex < rawGazeData.Length)
        {

            // Get the raw gaze direction and apply offset correction
            Vector3 gazeDirection = eyeGaze.transform.forward;

            // Adjust gaze direction based on head rotation
            gazeDirection = headTransform.rotation * gazeDirection;

            // Project gaze onto a plane in front of the camera
            Vector3 targetPosition = headTransform.position + (gazeDirection * (zOffsetCanvasToCamera));

            // Convert to local space relative to the head
            Vector3 localTargetPosition = headTransform.InverseTransformPoint(targetPosition);

            rawGazeData[calibrationIndex] = localTargetPosition;
            calibrationIndex++;
            ActivateNextCalibrationPoint();
        }
    }

    void ComputeGazeCalibration()
    {
        int count = rawGazeData.Length;

        // Compute average offset
        float sumOffsetX = 0f, sumOffsetY = 0f;
        for (int i = 0; i < count; i++)
        {
            sumOffsetX += targetPoints[i].x - rawGazeData[i].x;
            sumOffsetY += targetPoints[i].y - rawGazeData[i].y;
        }
        gazeXOffset = sumOffsetX / count;
        gazeYOffset = sumOffsetY / count;

        // Compute scale factors using points 3 and 4 (horizontal & vertical difference)
        float scaleX1 = (targetPoints[3].x - targetPoints[4].x) /
                        ((rawGazeData[3].x + gazeXOffset) - (rawGazeData[4].x + gazeXOffset));

        float scaleY1 = (targetPoints[3].y - targetPoints[4].y) /
                        ((rawGazeData[3].y + gazeYOffset) - (rawGazeData[4].y + gazeYOffset));

        // Compute scale factors using points 5 and 6 (horizontal & vertical difference)
        float scaleX2 = (targetPoints[5].x - targetPoints[6].x) /
                        ((rawGazeData[5].x + gazeXOffset) - (rawGazeData[6].x + gazeXOffset));

        float scaleY2 = (targetPoints[5].y - targetPoints[6].y) /
                        ((rawGazeData[5].y + gazeYOffset) - (rawGazeData[6].y + gazeYOffset));

        // Average the scale values for a more robust calibration
        scaleX = (scaleX1 + scaleX2) / 2f;
        scaleY = (scaleY1 + scaleY2) / 2f;

        //placeCenterMark();
        centerMarkCanvas.SetActive(true);
        centerMarkGlobal.SetActive(true);
    }

    void CheckIfFaceTrackingIsActive()
    {
        faceComponent = FindObjectOfType<OVRFaceExpressions>();

        if (faceComponent == null)
        {
            Debug.LogError("OVRFaceExpressions component not found in the scene. Please attach it to a GameObject.");
        }
    }

    void HideArrow()
    {
        buttonIndicator.SetActive(false);
    }

    void ToggleCentroids()
    {
        eyeTrackingEnabled = SelectionMode != 6; // only false if selctionMode = 6

        // Deactivating/Activating the Centerdots of each UI Element
        int eyeTrackOnly = LayerMask.NameToLayer("EyeTrackOnly"); // Get the layer index
        GameObject[] uiElements = FindObjectsOfType<GameObject>(true); // Find ALL objects, even inactive ones
        foreach (GameObject obj in uiElements)
        {
            if (obj.layer == eyeTrackOnly) { obj.SetActive(eyeTrackingEnabled); } // Deactivate or Activate All gameobjects inside the EyeTrackOnlyLayer
        }
    }

    void studyStartMethod()
    {
        // Increase the timer
        if (!studyStarted) studyStartTimer += Time.deltaTime;  // Increment the timer


        // Start Study
        if (studyStartSequence == 0 && studyStartTimer >= studyStartTime)
        {
            nextButtonTimer = -10;
            targetButton = 0;
            audioSource.PlayOneShot(studyStartSound);
            studyStartSequence++;
            ToggleCanvas(false);
            buttonIndicator.SetActive(false);
            studyStartSequence = 1;
            HideArrow();
            collisionSelectionButtonObject.SetActive(false);

        }

        else if (studyStartSequence == 1 && studyStartTimer >= studyStartTime + 3)
        {
            audioSource.PlayOneShot(studyStartSound);
            ToggleCanvas(false);
            buttonIndicator.SetActive(false);
            studyStartSequence = 2;
            HideArrow();
        }

        else if (studyStartSequence == 2 && studyStartTimer >= studyStartTime + 5)
        {
            CollisionUIButton.currentlyHighlighted = null;
            audioSource.PlayOneShot(studyStartSound);
            // Initialize filePath
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string fileName = $"{StudyID}_UI_log.csv"; // adds the StudyID into the filename
            filePath = Path.Combine(desktopPath, fileName);
            SaveCSVHeader("UI Study Started");  // Save the header once the study starts
            Debug.Log("Study started");
            studyStarted = true;
            studyStartSequence = 3;
            studyStartTimer = 0;
            nextButtonTimer = 0;
            targetButton = 0;
        }
    }

    public void placeCenterMark()
    {


        Vector3 positionInFront = referencedCamera.transform.position + referencedCamera.transform.forward * 2500;
        positionInFront.y = -375f; // Force Y to be exactly -375
        centerMarkGlobal.transform.position = positionInFront;
    }
}