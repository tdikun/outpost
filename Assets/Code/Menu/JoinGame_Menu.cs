﻿using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class JoinGame_Menu : MonoBehaviour
{
    public bool ShowDebugLogs = true;
    public GameObject RoomDetailsGUI_Prefab;

    private List<GameObject> RoomDetailsGUIList = new List<GameObject>();

    private void OnEnable()
    {
        // Establish listeners for all applicable events
        SessionManager.Instance.OnSMReceivedRoomListUpdate += RoomListUpdated_Event; ;
        SessionManager.Instance.OnSMJoinRoomFail += JoinRoomFail_Event;
        SessionManager.Instance.OnSMJoinedRoom += JoinedRoom_Event;

        // Update the current list of rooms available to choose from
        UpdateRoomList();

        // TODO: Display a waiting animation to show something is happening
    }

    private void OnDisable()
    {
        // Remove listeners for all applicable events
		if(SessionManager.Instance != null)
		{
        	SessionManager.Instance.OnSMReceivedRoomListUpdate -= RoomListUpdated_Event;
        	SessionManager.Instance.OnSMJoinRoomFail -= JoinRoomFail_Event;
        	SessionManager.Instance.OnSMJoinedRoom -= JoinedRoom_Event;
		}
    }

    #region OnClick

    public void Back_Click()
    {
        // Tell the MenuManager to transition back
        MenuManager.Instance.ShowMainMenu();
    }

    public void JoinRoom_Click(string buttonText)
    {
        SessionManager.Instance.JoinRoom(buttonText);
    }

    #endregion OnClick

    #region Events

    private void JoinRoomFail_Event(object[] codeAndMsg)
    {
        // TO DO: Refresh the list of rooms
    }

    private void JoinedRoom_Event()
    {
        // Tell the MenuManager to transition to the room
        MenuManager.Instance.ShowRoomDetailsMenu();
    }

    private void RoomListUpdated_Event()
    {
        UpdateRoomList();
    }

    #endregion Events

    /// <summary>
    /// Updates the UI displaying the current list of joinable rooms
    /// </summary>
    private void UpdateRoomList()
    {
        int index = 0;

        // Before updating the room list, destroy the current list
        DestroyRoomDetailGUIPrefabs();

        // Get a list of all joinable rooms
        RoomInfo[] roomList = SessionManager.Instance.GetRoomList();

        // Display each room currently in the lobby
        foreach (RoomInfo room in roomList)
        {
            // Instantiate row for each room and add it as a child of the JoinGame UI Panel
			RoomDetailsGUI roomDetails = Instantiate(RoomDetailsGUI_Prefab).GetComponent<RoomDetailsGUI>() as RoomDetailsGUI;
			roomDetails.UpdateDetails(room.name.Substring(0, room.name.IndexOf("(")), room.playerCount, room.maxPlayers, (room.open ? "Open" : "Closed"));
			//roomDetails.GetComponentInChildren<Text>().text = room.name.Substring(0, room.name.IndexOf("("));
			roomDetails.JoinButton.onClick.AddListener(delegate { JoinRoom_Click(room.name); });
			roomDetails.transform.SetParent(this.transform);
			roomDetails.transform.localScale = new Vector3(1, 1, 1);
			roomDetails.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 10 + (-35 * index));
			roomDetails.GetComponent<RectTransform>().localPosition = new Vector3(roomDetails.GetComponent<RectTransform>().localPosition.x, roomDetails.GetComponent<RectTransform>().localPosition.y, 0);
			roomDetails.transform.rotation = new Quaternion(0, 0, 0, 0);

            // Create a handle to all the prefabs that were created so we can destroy them later
			RoomDetailsGUIList.Add(roomDetails.gameObject);

            index++;
        }
    }

    /// <summary>
    /// Destroys all prefabs created for list of joinable rooms
    /// </summary>
    private void DestroyRoomDetailGUIPrefabs()
    {
        foreach (GameObject obj in RoomDetailsGUIList)
            Destroy(obj);
    }

    #region MessageHandling

    protected void Log(string message)
    {
        if (ShowDebugLogs)
            Debug.Log("[JoinGame_Menu] " + message);
    }

    protected void LogError(string message)
    {
        Debug.LogError("[JoinGame_Menu] " + message);
    }

    #endregion MessageHandling
}