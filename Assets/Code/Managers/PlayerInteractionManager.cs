﻿using UnityEngine;
using System.Collections;
using Settworks.Hexagons;
using UnityEngine.SceneManagement;

public class PlayerInteractionManager : MonoBehaviour
{
	private static PlayerInteractionManager instance;
	public bool ShowDebugLogs = true;

	public GameObject PlayerLocator;

	// Tower Placement
	private double LastTowerPlacementTime;
	private TowerData PlacementTowerData;
	private GameObject PlacementTowerPrefab;

	// Tower Selection
	public HexCoord SelectedTowerCoord { get; private set; }

	// Components
	private PhotonView ObjPhotonView;

	#region INSTANCE (SINGLETON)
	/// <summary>
	/// Singleton - There can be only one
	/// </summary>
	/// <value>The instance.</value>
	public static PlayerInteractionManager Instance
	{
		get
		{
			if (instance == null)
			{
				instance = GameObject.FindObjectOfType<PlayerInteractionManager>();
			}

			return instance;
		}
	}

	private void Awake()
	{
		instance = this;
	}

	#endregion

	// Use this for initialization
	void Start () 
	{
		SelectedTowerCoord = default(HexCoord);
		PlacementTowerData = null;
		LastTowerPlacementTime = Time.time;
		ObjPhotonView = PhotonView.Get(this);
	}
	
	// Update is called once per frame
	void Update () 
	{
		if (GameManager.Instance != null && GameManager.Instance.GameRunning)
		{
			var terrain = GameManager.Instance.TerrainMesh;

			Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
			RaycastHit hit;
			HexCoord coord;

			bool overTerrain = terrain.IntersectRay(ray, out hit, out coord);

			switch (PlayerManager.Instance.CurPlayer.Mode)
			{
			case PlayerMode.Selection:
				SelectionModeUpdate(overTerrain, coord);
				break;

			case PlayerMode.Placement:
				PlacementModeUpdate(overTerrain, coord);
				break;
			}
				
			// Always keep the player locator GameObject in front of the camera. The GameObject has a PhotonView attached to it for the RadarManager
			// to know which quadrant all players are located in.
			if (PlayerLocator)
			{
				PlayerLocator.transform.position = Camera.main.transform.position + (Camera.main.transform.forward * 10);
				PlayerLocator.transform.position = new Vector3(PlayerLocator.transform.position.x, PlayerLocator.transform.position.y, 0);
			}
		}
	}

	#region Mode-based User Interface Handling

	protected void SelectionModeUpdate(bool overTerrain, HexCoord coord)
	{
		// Right-click removes selection
		if (Input.GetMouseButtonDown(1))
		{
			RemoveSelection();
		}
		// Left-click selects
		else if (Input.GetMouseButton(0))
		{
			if (overTerrain && CanSelect(coord))
			{
				// Mouse click on existing tower
				Select(coord);
			}
			else
			{
				// Mouse click outside of placement area
				RemoveSelection();
			}
		}

		// Update highlight
		if (overTerrain && CanHighlight(coord))
		{
			Highlight(coord);
		}
		else
		{
			RemoveHighlight();
		}
	}
		
	protected void PlacementModeUpdate(bool overTerrain, HexCoord coord)
	{
		// No tower selection when we're in placement mode
		RemoveSelection();

		// Right-click cancels placement mode
		if (Input.GetMouseButtonDown(1))
		{
			// TODO: Move mode selection into its own function that worries about mode cleanup details
			PlayerManager.Instance.CurPlayer.Mode = PlayerMode.Selection;
			// Destroy the shell prefab
			if (PlacementTowerPrefab != null)
			{
				Destroy(PlacementTowerPrefab);
				PlacementTowerPrefab = null;
			}
			SelectionModeUpdate(overTerrain, coord);
			return;
		}

		// Is the player attempting to place a tower?
		if (Input.GetMouseButton(0))
		{
			// Is it a legal placement?
			if (overTerrain && CanBuild(PlacementTowerData, coord))
			{
				// FIXME: Tenatively place the tower but be prepared to roll back the change if the
				//        master reports a conflict with another player's tower placement at the same
				//        location at the same time
				LastTowerPlacementTime = Time.time;

				// Charge the player for building the tower
				PlayerManager.Instance.CurPlayer.PurchaseTower(PlacementTowerData.InstallCost);

				// Asset's unique ViewID
				int viewID = SessionManager.Instance.AllocateNewViewID();
				// Asset's display name
				string displayName = PlacementTowerData.DisplayName;

				// Tell all other players that an Enemy has spawned (SpawnEnemyAcrossNetwork is currently in GameManager.cs)
				// TODO: Move to a two-phase system, where the master confirms success before the tower is considered built?
				ObjPhotonView.RPC("SpawnTowerAcrossNetwork", PhotonTargets.All, displayName, coord, viewID);
			}
			else
			{
				// Display a notification if the player is out of money
				// FIXME: This code is repeated in CanBuildTower. Modify CanBuildTower to return a reason for the failure?
				if (PlayerManager.Instance.CurPlayer.Money < PlacementTowerData.InstallCost)
				{
					// This currently does not work. My intent is to display this message alongside the place where the player clicks
					NotificationManager.Instance.DisplayNotification(new NotificationData("", "Insufficient Funds", "InsufficientFunds", 0, Input.mousePosition));
				}
			}
		}

		// Update highlight
		if (overTerrain && CanHighlight(coord))
		{
			Highlight(coord);
		}
		else
		{
			RemoveHighlight();
		}
	}
	#endregion

	#region Coordinate Highlight

	/// <summary>
	/// Determines whether the player can highlight the specified coord in the current PlayerMode.
	/// </summary>
	/// <returns><c>true</c> if the player can highlight the specified coord; otherwise, <c>false</c>.</returns>
	/// <param name="coord">Coordinate to evaluate.</param>
	protected bool CanHighlight(HexCoord coord)
	{
		// Is the game initialized?
		if (GameManager.Instance == null)
		{
			return false;
		}

		// Is the game running?
		if (!GameManager.Instance.GameRunning)
		{
			return false;
		}

		// Is the coordinate within the placement range of our facility?
		if (!GameManager.Instance.TerrainMesh.IsBuildable(coord))
		{
			return false;
		}

		// Selection Mode
		if (PlayerManager.Instance.CurPlayer.Mode == PlayerMode.Selection)
		{
			// Is the coordinate already selected?
			if (coord == SelectedTowerCoord)
			{
				// TODO: Decide whether the selection overlay should really have priority over the highlight overlay
				return false;
			}
		}
		// Placement Mode
		else if (PlayerManager.Instance.CurPlayer.Mode == PlayerMode.Placement)
		{
			// Is there an existing tower?
			if (GameManager.Instance.TowerManager.HasTower(coord))
			{
				return false;
			}
		}

		return true;
	}

	private void Highlight(HexCoord coord)
	{
		var terrain = GameManager.Instance.TerrainMesh;
		var overlay = terrain.Overlays[TerrainOverlay.Highlight][PhotonNetwork.player.ID];

        overlay.Color = PlayerColors.colors[(int)SessionManager.Instance.GetPlayerInfo().customProperties["PlayerColorIndex"]];
        overlay.Set(coord);
		overlay.Show();

		if (PlayerManager.Instance.CurPlayer.Mode == PlayerMode.Placement)
		{
			// Show the selected tower (if applicable)
			SetShellTowerPosition(terrain.IntersectPosition((Vector3)coord.Position()));//coord.Position());
		}
	}

	private void RemoveHighlight()
	{
		var overlay = GameManager.Instance.TerrainMesh.Overlays[TerrainOverlay.Highlight][PhotonNetwork.player.ID];
		overlay.Hide();
	}
	#endregion

	#region Tower Selection

	/// <summary>
	/// Determines whether the player can select a tower at the specified coord.
	/// </summary>
	/// <returns><c>true</c> if the player can select a tower at the specified coord; otherwise, <c>false</c>.</returns>
	/// <param name="coord">Coordinate to evaluate.</param>
	protected bool CanSelect(HexCoord coord)
	{
		// Is the game initialized?
		if (GameManager.Instance == null)
		{
			return false;
		}

		// Is the game running?
		if (!GameManager.Instance.GameRunning)
		{
			return false;
		}

		// Are we in a suitable PlayerMode?
		if (PlayerManager.Instance.CurPlayer.Mode != PlayerMode.Selection)
		{
			return false;
		}

		// Is there an existing tower?
		if (!GameManager.Instance.TowerManager.HasTower(coord))
		{
			return false;
		}

		return true;
	}

	private void Select(HexCoord coord)
	{
		// Check if this is actually a deselection
		if (coord == default(HexCoord))
		{
			RemoveSelection();
			return;
		}

		// Check if this coordinate is already selected
		if (coord == SelectedTowerCoord)
		{
			// Don't do extra work or spam the network
			return;
		}

		var overlay = GameManager.Instance.TerrainMesh.Overlays[TerrainOverlay.Selection][PhotonNetwork.player.ID];

        overlay.Color = PlayerColors.colors[(int)SessionManager.Instance.GetPlayerInfo().customProperties["PlayerColorIndex"]];
        overlay.Set(coord);
		overlay.Show();

		// Deselect the old tower
		if (SelectedTowerCoord != coord)
		{
			GameObject previousTower;
			if (GameManager.Instance.TowerManager.TryGetTower(SelectedTowerCoord, out previousTower))
			{
				previousTower.GetComponent<Tower>().OnDeselect();
			}
		}

		// Update state
		SelectedTowerCoord = coord;

		// Select the new tower
		GameObject selectedTower;
		if (GameManager.Instance.TowerManager.TryGetTower(coord, out selectedTower))
		{
			selectedTower.GetComponent<Tower>().OnSelect();
		}

		// Tell all other players that this player has selected a tower
		ObjPhotonView.RPC("SelectTowerAcrossNetwork", PhotonTargets.Others, coord);
	}

	private void RemoveSelection()
	{
		// Check if we actually have a selection
		if (SelectedTowerCoord == default(HexCoord))
		{
			// Don't do extra work or spam the network
			return;
		}

		var overlay = GameManager.Instance.TerrainMesh.Overlays[TerrainOverlay.Selection][PhotonNetwork.player.ID];

		overlay.Hide();

		// Deselect the old tower
		GameObject selectedTower;
		if (GameManager.Instance.TowerManager.TryGetTower(SelectedTowerCoord, out selectedTower))
		{
			selectedTower.GetComponent<Tower>().OnDeselect();
		}

		// Update state
		SelectedTowerCoord = default(HexCoord);

		// Tell all other players that this player has deselected a tower
		ObjPhotonView.RPC("SelectTowerAcrossNetwork", PhotonTargets.Others, default(HexCoord));
	}

	[PunRPC]
	private void SelectTowerAcrossNetwork(HexCoord coord, PhotonMessageInfo info)
	{
		Log(info.sender.name + " selects " + coord.ToString());
		var overlay = GameManager.Instance.TerrainMesh.Overlays[TerrainOverlay.Selection][info.sender.ID];

		if (coord == default(HexCoord))
		{
			overlay.Hide();
		}
		else
        {
            overlay.Color = PlayerColors.colors[(int)info.sender.customProperties["PlayerColorIndex"]];
            overlay.Set(coord);
			overlay.Show();
		}
	}

	#endregion Tower Selection

	#region Tower Placement

	/// <summary>
	/// Determines whether the player can build the specified tower at the given coord.
	/// </summary>
	/// <returns><c>true</c> if the player can build the specified tower coord at the given coord; otherwise, <c>false</c>.</returns>
	/// <param name="tower">TowerData of the tower to evaluate.</param>
	/// <param name="coord">Coordinate of the tower to evaluate.</param>
	protected bool CanBuild(TowerData tower, HexCoord coord)
	{
		// Has tower data been provided?
		if (tower == null)
		{
			// TODO: Consider throwing an error instead of simply returning false
			return false;
		}

		// Is the game initialized?
		if (GameManager.Instance == null)
		{
			return false;
		}

		// Is the game running?
		if (!GameManager.Instance.GameRunning)
		{
			return false;
		}

		// Is the coordinate within the placement range of our facility?
		if (!GameManager.Instance.TerrainMesh.IsBuildable(coord))
		{
			return false;
		}

		// Is there an existing tower?
		if (GameManager.Instance.TowerManager.HasTower(coord))
		{
			return false;
		}

		// Has enough time passed since the last placement?
		// Towers cannot be repeatedly placed every frame
		if (Time.time - LastTowerPlacementTime <= 1.0f)
		{
			return false;
		}

		// Does the player have enough money to place the tower?
		if (PlayerManager.Instance.CurPlayer.Money < PlacementTowerData.InstallCost)
		{
			return false;
		}

		return true;
	}

	public void TowerSelectedForPlacement(TowerData towerData)
	{
		// Save the tower data
		PlacementTowerData = towerData;

		// Update the mode
		PlayerManager.Instance.CurPlayer.Mode = PlayerMode.Placement;

		// Create a "Look" quaternion that considers the Z axis to be "up" and that faces away from the base
		var rotation = Quaternion.LookRotation(new Vector3(10, 10, 0), new Vector3(0.0f, 0.0f, -1.0f));

		// Remove the old prefab (if applicable)
		if (PlacementTowerPrefab != null)
			Destroy(PlacementTowerPrefab);

		// Load the shell prefab to show
		PlacementTowerPrefab = Instantiate(Resources.Load("Towers/" + towerData.PrefabName + "_Shell"), Vector3.zero, rotation) as GameObject;

		// Set the range based on player prefab
		PlacementTowerPrefab.GetComponentInChildren<SpriteRenderer>().transform.localScale *= towerData.AdjustedRange;
	}

	public void SetShellTowerPosition(Vector3 newPosition)
	{
		// Only place the tower if a tower has been selected
		if (PlacementTowerPrefab != null)
		{
			PlacementTowerPrefab.transform.position = newPosition;
		}
	}

	[PunRPC]
	private void SpawnTowerAcrossNetwork(string displayName, HexCoord coord, int viewID, PhotonMessageInfo info)
	{
		// TODO: Coordinate tower spawning when two players build a tower in the same position at the same time.
		// TODO: Sanitize displayName if necessary. For example, make sure it matches against a white list. Also, if it contained "../blah", what would be the effect?

		if (GameManager.Instance.TowerManager.HasTower(coord))
		{
			LogError("Tower construction conflict! Potential inconsistent game state.");
		}

		var position = GameManager.Instance.TerrainMesh.IntersectPosition((Vector3)coord.Position());

		// Create a "Look" quaternion that considers the Z axis to be "up" and that faces away from the base
		var rotation = Quaternion.LookRotation(new Vector3(position.x, position.y, 0.0f), new Vector3(0.0f, 0.0f, -1.0f));

		// Instantiate a new Enemy
		GameObject newTower = Instantiate(Resources.Load("Towers/" + GameDataManager.Instance.FindTowerPrefabByDisplayName(displayName)), position, rotation) as GameObject;
		// Add a PhotonView to the Tower
		newTower.AddComponent<PhotonView>();
		// Set Tower's PhotonView to match the Master Client's PhotonView ID for this GameObject (these IDs must match for networking to work)
		newTower.GetComponent<PhotonView>().viewID = viewID;

		// Store the tower in AnalyticsManager
		if (GameManager.Instance.GameRunning)
		{
			AnalyticsManager.Instance.Assets.AddAsset("Tower", displayName, viewID, position);
		}


		// The Prefab doesn't contain the correct default data. Set the Tower's default data now
		newTower.GetComponent<Tower>().SetTowerData(GameDataManager.Instance.FindTowerDataByDisplayName(displayName), info.sender);

		if (info.sender.isLocal)
		{
			// Change the player mode
			PlayerManager.Instance.CurPlayer.Mode = PlayerMode.Selection;
			// Deselect the selected tower (force the user to select a new one)
			PlacementTowerData = null;
			// Destroy the shell prefab
			Destroy(PlacementTowerPrefab);
			PlacementTowerPrefab = null;
		}
	}

	#endregion Tower Placement

	public void OnLevelWasLoaded(int level)
    {
        // All levels MUST begin with a defined prefix for this to work properly
		if (SceneManager.GetActiveScene().name.StartsWith("Level"))
        {
			// Instantiate a player locator point that is used for the allies' Radar
			PlayerLocator = SessionManager.Instance.InstantiateObject("PlayerLocator", new Vector3(0, 0, 0), Quaternion.identity) as GameObject;
			PlayerLocator.name = PlayerManager.Instance.CurPlayer.Username;
        }
    }

	#region MessageHandling

	protected void Log(string message)
	{
		if (ShowDebugLogs)
			Debug.Log("[PlayerInteractionManager] " + message);
	}

	protected void LogError(string message)
	{
		Debug.LogError("[PlayerInteractionManager] " + message);
	}

	#endregion MessageHandling
}
