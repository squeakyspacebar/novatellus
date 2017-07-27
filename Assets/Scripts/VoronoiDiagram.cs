using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using csDelaunay;

// Metadata class representing physiographic section data.
public class Section {
	public Cell CellObject { get; set; }
	public float Elevation { get; set; }
	// Flag whether the game object tied to the section needs to update its meshes.
	public bool NeedUpdate { get; set; }

	public Section(float elevation = 0.0f) {
		this.Elevation = elevation;
	}
}

// Metadata class representing tectonic plate boundary data.
public class Boundary {
	public Vector2f Stress { get; set; }
	public float Parallel { get; set; }
	public float Orthogonal { get; set; }
	public bool Divergent { get; set; }

	public Boundary() {
		this.Stress = new Vector2f(0.0f, 0.0f);
	}
}

// Metadata class representing tectonic plate data.
public class TectonicPlate {
	public Cell CellObject { get; set; }
	public Vector2f Force { get; set; }
	public HashSet<Site> Sections { get; set; }
	public bool Oceanic { get; set; }
	public float Density { get; set; }
	public float Elevation;
	public float MaxElevation;

	public TectonicPlate() {
		this.Oceanic = true;
		this.Force = new Vector2f(0.0f, 0.0f);
		this.Sections = new HashSet<Site>();
		this.Elevation = 0.0f;
		this.MaxElevation = 0.0f;
	}
}

public class VoronoiDiagram : MonoBehaviour {
	// Unity references.
	public Material DefaultMat;
	public Material LineMat;
	public Material OceanMat;
	public Transform CellPrefab; // GameObject representing Voronoi cell.

	// Bounds for generating Voronoi diagrams in local coordinates.
	private Rectf bounds;
	public float BoundWidth;
	public float BoundHeight;
	public float BoundOffsetX;
	public float BoundOffsetY;

	// Dimensions of map.
	public int Width;
	public int Height;

	// Map display controls.
	private enum displayModes {Plate, Elevation};
	private int displayMode;
	private enum spectralModes {Monochrome, Color};
	private int spectralMode;
	private HashSet<Cell> activeCells = new HashSet<Cell>();

	// For elevation generation.
	private float seafloor = 0.365f; // Average seafloor depth.
	private float sealevel = 0.55f;
	private bool displayAll;
	private System.Random randGen;
	private Dictionary<Site, Section> sectionMetadata = new Dictionary<Site, Section>();
	private Dictionary<Site, TectonicPlate> plateMetadata = new Dictionary<Site, TectonicPlate>();
	private Dictionary<Edge, Boundary> plateBoundaries = new Dictionary<Edge, Boundary>();
	private List<TectonicPlate> plates = new List<TectonicPlate>();
	private List<GameObject> coastObjects = new List<GameObject>();
	private List<GameObject> boundaryObjects = new List<GameObject>();
	public Voronoi SectionDiagram;

	private void Start() {
		// Load any required resources.
		if (this.DefaultMat == null) {
			this.DefaultMat = Resources.Load("Materials/Default", typeof(Material)) as Material;
		}
		if (this.LineMat == null) {
			this.LineMat = Resources.Load("Materials/Line", typeof(Material)) as Material;
		}
		if (this.OceanMat == null) {
			this.OceanMat = Resources.Load("Materials/Ocean", typeof(Material)) as Material;
		}

		// Initialize random number generator.
		this.randGen = new System.Random();

		// Set details about map rendering bounds.
		Renderer renderer = GetComponent<Renderer>();
		this.BoundWidth = renderer.bounds.size.x;
		this.BoundHeight = renderer.bounds.size.y;
		this.BoundOffsetX = renderer.bounds.size.x / 2;
		this.BoundOffsetY = renderer.bounds.size.y / 2;

		// Set map resolution.
		this.Width = 1024;
		this.Height = 1024;

		// Create the bounds of the Voronoi diagram.
		// Use Rectf instead of Rect; it's a struct just like Rect and does pretty much the same,
		// but like that it allows you to run the delaunay library outside of unity (which mean also in another tread).
		this.bounds = new Rectf(0.0f, 0.0f, this.Width, this.Height);

		this.displayMode = (int)displayModes.Elevation;
		this.spectralMode = (int)spectralModes.Color;
		this.displayAll = false;

		// Create underlying "physiographic sections" first.
		this.GenerateSections(8000);
		// Create plates and assign sections to plates.
		this.GenerateTectonicPlates(20);
		// Calculate stress on plate boundaries.
		this.CalculateStress();
		this.RefreshSections();
	}

	private void Update() {
		// See InputManager under Edit->Project Settings->Input.
		if (Input.GetAxis("Mouse ScrollWheel") > 0) { // Forward.
			Camera.main.orthographicSize -= 0.1f;
			this.UpdateMainCamera();
		} else if (Input.GetAxis("Mouse ScrollWheel") < 0) { // Backward.
			Camera.main.orthographicSize += 0.1f;
			this.UpdateMainCamera();
		}
	}

	// Provides a developer menu to help test features.
	private void OnGUI() {
		if (GUILayout.Button("Display Plate Map")) {
			this.displayMode = (int)displayModes.Plate;
			this.ClearScreen();
			this.Render(true);
		}
		if (GUILayout.Button("Display Plate Boundaries")) {
			this.displayMode = (int)displayModes.Plate;
			this.DrawBoundaries();
		}
		if (GUILayout.Button("Display Elevation Map")) {
			this.displayMode = (int)displayModes.Elevation;
			this.ClearScreen();
			this.Render(this.displayAll);
		}
		if (GUILayout.Button("Spectral Heightmap")) {
			this.displayMode = (int)displayModes.Elevation;
			this.spectralMode = (int)spectralModes.Color;
			this.ClearScreen();
			this.Render(this.displayAll);
		}
		if (GUILayout.Button("Monochrome Heightmap")) {
			this.displayMode = (int)displayModes.Elevation;
			this.spectralMode = (int)spectralModes.Monochrome;
			this.ClearScreen();
			this.Render(this.displayAll);
		}
		if (GUILayout.Button("Reset Elevation")) {
			this.ResetElevation(this.seafloor);
			this.RefreshSections();
			this.ClearScreen();
		}
		if (GUILayout.Button("Generate New Plate Map")) {
			this.displayMode = (int)displayModes.Plate;
			this.ResetPlates();
			this.GenerateTectonicPlates(20);
			this.CalculateStress();
			this.RefreshSections();
			this.ClearScreen();
			this.Render(true);
		}
		if (GUILayout.Button("Generate New Elevation Map")) {
			this.displayMode = (int)displayModes.Elevation;
			this.ResetElevation(this.seafloor);
			this.CreateUplift();
			this.RefreshSections();
			this.ClearScreen();
			this.Render(this.displayAll);
		}
		if (GUILayout.Button("Generate Central Blob")) {
			this.GenerateCentralBlob();
			this.RefreshSections();
			this.ClearScreen();
			this.Render(this.displayAll);
		}
		if (GUILayout.Button("Generate Random Blob")) {
			this.GenerateRandomBlobs(1);
			this.RefreshSections();
			this.ClearScreen();
			this.Render(this.displayAll);
		}
		if (GUILayout.Button("Toggle Bathymetry")) {
			this.displayMode = (int)displayModes.Elevation;
			this.displayAll = !this.displayAll;
			this.ClearScreen();
			this.Render(this.displayAll);
		}
	}

	// Generates tectonic plates by assigning sections to plates via a pseudo-simultaneous flood-fill algorithm.
	// Also generates a global list of plate boundaries.
	private void GenerateTectonicPlates(int numPlates) {
		Queue<Site> updateSites = new Queue<Site>();
		Dictionary<Site, bool> queuedSites = new Dictionary<Site, bool>();
		List<Vector2f> seeds = this.CreateRandomPoints(numPlates);

		int numOceanPlates = 0; // Counter for number of oceanic plates generated.
		foreach (Vector2f seed in seeds) {
			// Select a random section to be a plate spawn point.
			Site sectionSite = GetClosestSection(seed);

			// Select a different section if the one we picked is already in the list.
			while (this.plateMetadata.ContainsKey(sectionSite)) {
				sectionSite = GetClosestSection(this.CreateRandomPoints(1)[0]);
			}

			TectonicPlate plate = new TectonicPlate();

			// Determine whether the plate should be oceanic or continental.
			if ((float)numOceanPlates / numPlates < 0.7f) {
				plate.Oceanic = true;
				plate.Density = 0.4f;
				// Set elevation parameters for oceanic plates.
				plate.Elevation = this.seafloor;
				plate.MaxElevation = this.sealevel + 0.21f;
			} else {
				plate.Oceanic = false;
				plate.Density = 0.125f;
				// Set elevation parameters for continental plates.
				plate.Elevation = this.sealevel;
				plate.MaxElevation = 1.0f;
			}
			numOceanPlates++;

			// Create bi-directional mappings between sections and plates.
			this.plateMetadata.Add(sectionSite, plate);
			plate.Sections.Add(sectionSite);
			this.plates.Add(plate);

			// Generate a random movement vector for the plate.
			float forceX = (float)this.randGen.NextDouble();
			float forceY = (float)this.randGen.NextDouble();
			plate.Force = new Vector2f(forceX, forceY);
			plate.Force.Normalize();

			updateSites.Enqueue(sectionSite);
			queuedSites.Add(sectionSite, true);
		}

		// Assign sections to plates and build a list of boundary edges for each plate.
		while (updateSites.Any()) {
			Site sectionSite = updateSites.Dequeue();
			TectonicPlate plateData = this.plateMetadata[sectionSite];
			Dictionary<Site, Edge> sectionEdges = sectionSite.NeighborSiteEdges();

			foreach (Site neighborSite in sectionSite.NeighborSites()) {
				// Don't add any sites for processing that have already been queued.
				bool alreadyQueued = false;
				queuedSites.TryGetValue(neighborSite, out alreadyQueued);
				if (!alreadyQueued) {
					// Assimilate any available neighboring sections.
					this.plateMetadata.Add(neighborSite, plateData);
					plateData.Sections.Add(neighborSite);

					updateSites.Enqueue(neighborSite);
					queuedSites.Add(neighborSite, true);
				} else {
					// If the neighbor doesn't share the same plate, the edge is a boundary.
					if (plateData != this.plateMetadata[neighborSite]) {
						// Get boundary edge.
						Edge edge;
						if (sectionEdges.TryGetValue(neighborSite, out edge)) {
							// Add boundary edge to list for current plate.
							try {
								this.plateBoundaries.Add(edge, new Boundary());
							} catch (System.ArgumentException ex) {
								// Don't need to worry about failed attempts to add duplicates.
							}
						}
					}
				}
			}
		}
	}

	private void GenerateSections(int numberOfSections) {
		List<Vector2f> seeds = this.CreateRandomPoints(numberOfSections);
		this.SectionDiagram = new Voronoi(seeds, this.bounds, 10);

		foreach (Site site in this.SectionDiagram.Sites) {
			// Set default elevation to sea floor.
			Section sectionData = new Section(this.seafloor);
			this.sectionMetadata.Add(site, sectionData);

			// Create Voronoi game object corresponding to section.
			GameObject go = Instantiate(CellPrefab,
				this.VoronoiTransformPoint(site.Coord),
				Quaternion.identity).gameObject;
			go.transform.SetParent(gameObject.transform);
			Cell cellObject = go.GetComponent(typeof(Cell)) as Cell;
			cellObject.SetSite(site);
			sectionData.CellObject = cellObject;

			// Create new meshes for each cell object.
			cellObject.GetComponent<MeshFilter>().sharedMesh = new Mesh();
			cellObject.GetComponent<MeshCollider>().sharedMesh = new Mesh();

			Renderer renderer = cellObject.GetComponent<Renderer>();
			renderer.enabled = false;
		}
	}

	public void RefreshSections() {
		// Recalculate Voronoi diagram.
		this.SectionDiagram.Update();
		foreach (Site sectionSite in this.SectionDiagram.Sites) {
			this.sectionMetadata[sectionSite].NeedUpdate = true;
		}
	}

	// Set the stresses on plate boundaries.
	private void CalculateStress() {
		foreach (KeyValuePair<Edge, Boundary> kv in this.plateBoundaries) {
			Edge edge = kv.Key;

			if (edge.LeftVertex != null && edge.RightVertex != null) {
				Boundary boundary = kv.Value;

				Vector2f edgeVector = edge.LeftVertex.Coord - edge.RightVertex.Coord;
				edgeVector.Normalize();

				Vector2f leftMotion = this.plateMetadata[edge.LeftSite].Force;
				Vector2f rightMotion = this.plateMetadata[edge.RightSite].Force;
				boundary.Stress = (leftMotion - rightMotion);
				// Get the component of stress parallel to the fault.
				boundary.Parallel = Mathf.Abs(boundary.Stress.Dot(edgeVector));
				// Get the component of stress perpendicular to the fault.
				boundary.Orthogonal = Mathf.Abs(boundary.Stress.Cross(edgeVector));

				// Calculate boundary divergence.
				Vector2f direction = edge.RightSite.Coord - edge.LeftSite.Coord;
				float directionality = direction.Unit.Dot(rightMotion.Unit);
				if (directionality < 0) {
					boundary.Divergent = false;
				} else {
					boundary.Divergent = true;
				}
			}
		}
	}

	// Simulates tectonic uplift based on plate boundary stress conditions.
	private void CreateUplift() {
		Dictionary<Site, float> initialSites = new Dictionary<Site, float>();

		foreach (KeyValuePair<Edge, Boundary> kv in this.plateBoundaries) {
			Edge edge = kv.Key;

			if (edge.LeftSite == null || edge.RightSite == null) {
				continue;
			}

			Boundary boundary = kv.Value;
			TectonicPlate leftPlate = this.plateMetadata[edge.LeftSite];
			TectonicPlate rightPlate = this.plateMetadata[edge.RightSite];

			float leftElevation;
			float rightElevation;
			if ((boundary.Orthogonal > boundary.Parallel)
				&& !boundary.Divergent
				&& (boundary.Stress.Magnitude > 0.1f)) {
				float minElevation = Mathf.Max(leftPlate.Elevation, rightPlate.Elevation);
				float maxElevation = Mathf.Max(leftPlate.MaxElevation, rightPlate.MaxElevation);
				leftElevation = boundary.Orthogonal / boundary.Stress.Magnitude *
					(maxElevation - minElevation) + minElevation;
				rightElevation = boundary.Orthogonal / boundary.Stress.Magnitude *
					(maxElevation - minElevation) + minElevation;
			} else if (((boundary.Parallel > boundary.Orthogonal) || boundary.Divergent)
				&& (boundary.Stress.Magnitude > 0.1f)) {
				float minElevation = Mathf.Max(leftPlate.Elevation, rightPlate.Elevation);
				float maxElevation = Mathf.Max(leftPlate.MaxElevation, rightPlate.MaxElevation);
				leftElevation = boundary.Parallel / boundary.Stress.Magnitude * 0.25f *
					(maxElevation - minElevation) + minElevation;
				rightElevation = boundary.Parallel / boundary.Stress.Magnitude * 0.25f *
					(maxElevation - minElevation) + minElevation;
			} else {
				leftElevation = (leftPlate.Elevation + rightPlate.Elevation) * 0.5f;
				rightElevation = leftElevation;
			}

			try {
				// Preserve the highest elevation (generated by the highest stress).
				if (!initialSites.ContainsKey(edge.LeftSite) || (initialSites.ContainsKey(edge.LeftSite)
					&& (leftElevation > initialSites[edge.LeftSite]))) {
					initialSites.Add(edge.LeftSite, leftElevation);
				}
				if (!initialSites.ContainsKey(edge.RightSite) || (initialSites.ContainsKey(edge.RightSite)
					&& (rightElevation > initialSites[edge.RightSite]))) {
					initialSites.Add(edge.RightSite, rightElevation);
				}
			} catch (System.ArgumentException ex) {
				// Ignore.
			}
		}

		this.GenerateElevation(initialSites);
	}

	// Elevation generation testing function.
	private void GenerateCentralBlob() {
		Dictionary<Site, float> initialSites = new Dictionary<Site, float>();

		// Get section closest to the center of the map.
		Vector2f mapCenter = new Vector2f(this.Width * 0.5f, this.Height * 0.5f);
		Site centerSite = GetClosestSection(mapCenter);

		// Create initial elevation and load elevation seeding site into queue.
		//float height = (float)randGen.NextDouble() * (1.0f - this.sealevel) + this.sealevel;
		float height = 1.0f;
		initialSites.Add(centerSite, height);

		GenerateElevation(initialSites, true);
	}

	// Elevation generation testing function.
	private void GenerateRandomBlobs(int n) {
		Dictionary<Site, float> initialSites = new Dictionary<Site, float>();

		int siteCount = this.SectionDiagram.Sites.Count;
		for (int i = 0; i < n; i++) {
			int siteIndex = this.randGen.Next(siteCount);
			Site site = this.SectionDiagram.Sites[siteIndex];
			//float height = (float)randGen.NextDouble() * (1.0f - this.sealevel) + this.sealevel;
			float height = 1.0f;

			do {
				try {
					initialSites.Add(site, height);
				} catch (System.ArgumentException ex) {
					siteIndex = this.randGen.Next(siteCount);
					site = this.SectionDiagram.Sites[siteIndex];
				}
			} while (!initialSites.ContainsKey(site));
		}

		GenerateElevation(initialSites, true);
	}

	private void GenerateElevation(Dictionary<Site, float> initialSiteElevations, bool pointElevation = false) {
		float elevationCutoff = 0.01f;

		Queue<Site> updateSites = new Queue<Site>();
		Queue<float> elevations = new Queue<float>();
		Dictionary<Site, bool> queuedSites = new Dictionary<Site, bool>();

		// Queue initial sites for processing.
		foreach (KeyValuePair<Site, float> kv in initialSiteElevations) {
			Site site = kv.Key;
			updateSites.Enqueue(site);
			elevations.Enqueue(initialSiteElevations[site]);
			queuedSites.Add(site, true);
		}

		// Propagate elevation.
		while (updateSites.Any()) {
			Site site = updateSites.Dequeue();
			TectonicPlate sitePlate = this.plateMetadata[site];
			float newElevation = elevations.Dequeue();

			// Preserve any elevation that has been generated before.
			if (this.sectionMetadata[site].Elevation < newElevation) {
				this.sectionMetadata[site].Elevation = newElevation;
			}

			foreach (KeyValuePair<Site, Edge> kv in site.NeighborSiteEdges()) {
				Site neighborSite = kv.Key;
				TectonicPlate neighborPlate = this.plateMetadata[neighborSite];

				// Don't queue any sites for processing that have already been queued.
				bool alreadyQueued = false;
				queuedSites.TryGetValue(neighborSite, out alreadyQueued);
				if (!alreadyQueued) {
					// Generate perturbed elevations based on parent elevation.
					float range = neighborPlate.Density;
					float perturbation;
					if (pointElevation) {
						// Constant elevation decay rate primarily for testing.
						perturbation = 0.95f;
					} else {
						perturbation = (float)randGen.NextDouble() * range + 1.1f - range;
					}
					float neighborHeight = newElevation * perturbation;

					if (newElevation > elevationCutoff) {
						updateSites.Enqueue(neighborSite);
						elevations.Enqueue(neighborHeight);
						queuedSites.Add(neighborSite, true);
					}
				}
			}
		}
	}

	public void Render(bool displayAll = false) {
		if (this.displayMode == (int)displayModes.Elevation) {
			this.RenderElevation(displayAll);
		} else if (this.displayMode == (int)displayModes.Plate) {
			this.RenderPlates(displayAll);
		}
	}

	private void RenderElevation(bool displayAll = false) {
		// Set background to base color for display mode.
		Renderer mapRenderer = gameObject.GetComponent<Renderer>();
		if (this.spectralMode == (int)spectralModes.Color) {
			mapRenderer.material.color = Color.blue;
		} else if (this.spectralMode == (int)spectralModes.Monochrome) {
			mapRenderer.material.color = Color.black;
		}
		mapRenderer.enabled = true;

		foreach (Site sectionSite in this.SectionDiagram.Sites) {
			Section sectionData = this.sectionMetadata[sectionSite];
			Cell cellObject = sectionData.CellObject;
			Renderer rend = cellObject.GetComponent<Renderer>();

			if (displayAll || (sectionData.Elevation >= this.sealevel)) {
				// Only update the mesh if necessary.
				if (sectionData.NeedUpdate) {
					// Convert cell vertices to local coordinates for the mesh.
					List<Vector3> meshVertices = this.GetMeshVertices(cellObject, sectionSite.Region(this.bounds));
					this.Triangulate(cellObject, meshVertices);
					sectionData.NeedUpdate = false;
				}

				// Update elevation heatmap.
				if (this.spectralMode == (int)spectralModes.Color) {
					if (sectionData.Elevation > sealevel) {
						/*float intensity = Mathf.Max(0.0f, (sectionData.Elevation - this.sealevel) /
							(1.0f - this.sealevel));*/
						rend.material.color = SpectralHeat(sectionData.Elevation);
					} else if (sectionData.Elevation >= seafloor) {
						Color32 lo = new Color32(0, 0, 128, 255);
						Color32 hi = new Color32(0, 255, 255, 255);
						rend.material.color = Color.Lerp(lo, hi, (sectionData.Elevation - this.seafloor) /
							(this.sealevel - this.seafloor));
					} else {
						Color32 lo = new Color32(0, 0, 32, 255);
						Color32 hi = new Color32(0, 0, 64, 255);
						rend.material.color = Color.Lerp(lo, hi, sectionData.Elevation / this.sealevel);
					}
				} else if (this.spectralMode == (int)spectralModes.Monochrome) {
					rend.material.color = MonochromeHeat(sectionData.Elevation);
				}
				rend.enabled = true;

				this.activeCells.Add(cellObject);
			} else {
				rend.enabled = false;
			}
		}

		// Render an outline for coasts.
		DrawCoastline();
	}

	private void RenderPlates(bool displayAll = false) {
		foreach (TectonicPlate plate in this.plates) {
			foreach (Site sectionSite in plate.Sections) {
				Section sectionData = this.sectionMetadata[sectionSite];

				if (displayAll || (sectionData.Elevation > this.sealevel)) {
					Cell cellObject = sectionData.CellObject;

					// Only update the mesh if necessary.
					if (sectionData.NeedUpdate) {
						// Convert cell vertices to local coordinates for the mesh.
						List<Vector3> meshVertices = this.GetMeshVertices(cellObject, sectionSite.Region(this.bounds));
						this.Triangulate(cellObject, meshVertices);

						sectionData.NeedUpdate = false;
					}

					Renderer renderer = cellObject.GetComponent<Renderer>();
					if (displayAll) {
						renderer.material.color = Color.gray;
					}
					renderer.enabled = true;

					this.activeCells.Add(cellObject);
				}
			}
		}

		this.DrawBoundaries();
		if (!displayAll) {
			this.DrawCoastline();
		}
	}

	private void DrawCoastline() {
		List<Vector2f> coastline = new List<Vector2f>();

		foreach (Site site in this.SectionDiagram.Sites) {
			float elevation = this.sectionMetadata[site].Elevation;
			if (elevation >= this.sealevel) {
				foreach (Edge e in site.Edges) {
					if (e.Visible && e.LeftSite != null && e.RightSite != null) {
						Site check = e.LeftSite;
						if (ReferenceEquals(check, site)) {
							check = e.RightSite;
						}
						if (this.sectionMetadata[check].Elevation < this.sealevel) {
							if (TriangleIsCCW(check.Coord, e.ClippedVertices[LR.LEFT],
								e.ClippedVertices[LR.RIGHT])) {
								coastline.Add(e.ClippedVertices[LR.LEFT]);
								coastline.Add(e.ClippedVertices[LR.RIGHT]);
							} else {
								coastline.Add(e.ClippedVertices[LR.RIGHT]);
								coastline.Add(e.ClippedVertices[LR.LEFT]);
							}
						}
					}
				}
			}
		}

		Vector3[] lineVertices = GetLineVertices(coastline);

		// Update coastlines.
		int numVertices = lineVertices.Length;
		for (int i = 0; i < numVertices; i += 2) {
			GameObject go = new GameObject();
			go.transform.SetParent(gameObject.transform);
			go.AddComponent<LineRenderer>();
			this.coastObjects.Add(go);

			LineRenderer lineRend = go.GetComponent<LineRenderer>();
			lineRend.enabled = true;
			lineRend.useWorldSpace = true;
			lineRend.textureMode = LineTextureMode.Stretch;
			lineRend.material = LineMat;
			lineRend.material.color = Color.black;
			lineRend.widthMultiplier = 0.003f;
			lineRend.positionCount = 2;
			lineRend.SetPosition(0, lineVertices[i]);
			lineRend.SetPosition(1, lineVertices[i + 1]);
		}
	}

	private void DrawBoundaries() {
		List<Vector2f> boundaryVertices = new List<Vector2f>();
		List<Color> boundaryColors = new List<Color>();

		foreach (KeyValuePair<Edge, Boundary> kv in this.plateBoundaries) {
			Edge edge = kv.Key;
			Boundary boundary = kv.Value;

			if (edge.Visible) {
				if (!displayAll && (this.sectionMetadata[edge.LeftSite].Elevation > this.sealevel
					|| this.sectionMetadata[edge.RightSite].Elevation > this.sealevel)) {
					boundaryVertices.Add(edge.ClippedVertices[LR.LEFT]);
					boundaryVertices.Add(edge.ClippedVertices[LR.RIGHT]);
				} else {
					boundaryVertices.Add(edge.ClippedVertices[LR.LEFT]);
					boundaryVertices.Add(edge.ClippedVertices[LR.RIGHT]);
				}
			}

			boundaryColors.Add(SpectralHeat(boundary.Stress.Magnitude));
		}

		Vector3[] lineVertices = GetLineVertices(boundaryVertices);

		// Update boundaries.
		int numVertices = boundaryVertices.Count;
		for (int i = 0, c = 0; i < numVertices; i += 2, c++) {
			GameObject lineObject = new GameObject();
			lineObject.transform.SetParent(gameObject.transform);
			lineObject.transform.SetParent(gameObject.transform);
			lineObject.AddComponent<LineRenderer>();
			this.boundaryObjects.Add(lineObject);

			LineRenderer lineRenderer = lineObject.GetComponent<LineRenderer>();
			lineRenderer.enabled = true;
			lineRenderer.useWorldSpace = true;
			lineRenderer.textureMode = LineTextureMode.Stretch;
			lineRenderer.material = LineMat;
			lineRenderer.material.color = boundaryColors[c];
			lineRenderer.widthMultiplier = 0.003f;
			lineRenderer.positionCount = 2;
			lineRenderer.SetPosition(0, lineVertices[i]);
			lineRenderer.SetPosition(1, lineVertices[i + 1]);
		}
	}

	public void ClearScreen() {
		// Turn off rendering for each section, then clear the list of active objects.
		foreach (Cell cellObject in this.activeCells) {
			Renderer rend = cellObject.GetComponent<Renderer>();
			rend.enabled = false;
			LineRenderer lineRend = cellObject.GetComponent<LineRenderer>();
			lineRend.enabled = false;
		}
		this.activeCells.Clear();
		this.ClearLines(this.coastObjects);
		this.ClearLines(this.boundaryObjects);
	}

	private void ClearLines(List<GameObject> lineObjects) {
		foreach (GameObject lineObject in lineObjects) {
			Destroy(lineObject.GetComponent<LineRenderer>().material);
			Destroy(lineObject);
		}
		lineObjects.Clear();
	}

	private void ResetPlates() {
		this.ClearLines(this.boundaryObjects);
		this.plateBoundaries.Clear();
		this.plateMetadata.Clear();
		this.plates.Clear();
	}

	private void ResetElevation(float elevation) {
		foreach (Site site in this.SectionDiagram.Sites) {
			this.sectionMetadata[site].Elevation = elevation;
		}
	}

	private Site GetClosestSection(Vector2f point) {
		List<Site> sites = this.SectionDiagram.Sites;
		int siteCount = sites.Count;
		float minDistSq = float.PositiveInfinity;
		Site closestSite = null;

		for (int i = 0; i < siteCount; i++) {
			float distSq = Vector2f.DistanceSquare(point, sites[i].Coord);
			if (distSq < minDistSq) {
				closestSite = sites[i];
				minDistSq = distSq;
			};
		}

		return closestSite;
	}

	// Generates list of unique random points.
	private List<Vector2f> CreateRandomPoints(int n) {
		HashSet<Vector2f> points = new HashSet<Vector2f>();
		for (int i = 0; i < n; i++) {
			float x = (float)this.randGen.Next(this.Width);
			float y = (float)this.randGen.Next(this.Height);

			while (!points.Add(new Vector2f(x, y))) {
				x = (float)this.randGen.Next(this.Width);
				y = (float)this.randGen.Next(this.Height);
			}
		}

		return points.ToList();
	}

	// Convert a Vector2f into Vector3 for use with Unity with coordinate transform.
	public Vector3 VoronoiTransformPoint(Vector2f point, float defaultZ = 0.0f) {
		return new Vector3(point.x / (float)this.Width - this.BoundOffsetX,
			point.y / (float)this.Height - this.BoundOffsetY, defaultZ);
	}

	private List<Vector3> GetMeshVertices(Cell cell, List<Vector2f> vertices) {
		// Convert cell vertices to local coordinates for the mesh.
		List<Vector3> meshVertices = new List<Vector3>();
		foreach (Vector2f vertex in vertices) {
			meshVertices.Add(cell.transform.InverseTransformPoint(VoronoiTransformPoint(vertex, -0.001f)));
		}
		return meshVertices;
	}

	private Vector3[] GetLineVertices(List<Vector2f> vertices, float z = -0.002f) {
		// Convert cell vertices to local coordinates for the mesh.
		Vector3[] meshVertices = new Vector3[vertices.Count];
		for (int i = 0; i < meshVertices.Length; i++) {
			meshVertices[i] = this.VoronoiTransformPoint(vertices[i], z);
		}
		return meshVertices;
	}

	// Creates the mesh triangle array from a set of given vertices and updates the meshes of the given object.
	// Assumes the vertices are already ordered and in local coordinates.
	private void Triangulate(Cell cellObject, List<Vector3> meshVertices) {
		// Calculate triangle indices.
		List<int> indices = new List<int>();
		for (int i = 0; i < meshVertices.Count - 2; i++) {
			indices.Add(0);
			indices.Add(i + 1);
			indices.Add(i + 2);
		}
		indices.Reverse();
		int[] meshTriangles = indices.ToArray();

		// Refresh display mesh.
		MeshFilter meshFilter = cellObject.GetComponent<MeshFilter>();
		Mesh mesh = meshFilter.sharedMesh;
		mesh.Clear();
		mesh.SetVertices(meshVertices);
		mesh.SetTriangles(meshTriangles, 0);
		mesh.RecalculateNormals();
		meshFilter.sharedMesh = null;
		meshFilter.sharedMesh = mesh;

		// Refresh collision mesh.
		MeshCollider meshCollider = cellObject.GetComponent<MeshCollider>();
		Mesh cmesh = meshCollider.sharedMesh;
		cmesh.Clear();
		cmesh.SetVertices(meshVertices);
		cmesh.SetTriangles(meshTriangles, 0);
		cmesh.RecalculateNormals();
		meshCollider.sharedMesh = null;
		meshCollider.sharedMesh = cmesh;
	}

	// Determines whether or not a set of three points is in counter-clockwise order.
	private static bool TriangleIsCCW(Vector2f a, Vector2f b, Vector2f c) {
		float det = ((a.x - c.x) * (b.y - c.y)) - ((a.y - c.y) * (b.x - c.x));

		if (det > 0) {
			return true;
		} else {
			return false;
		}
	}

	private Color SpectralHeat(float interp) {
		float r = Mathf.Max((interp - 0.5f) / 0.5f, 0.0f);
		float g = 1.0f - Mathf.Abs(0.5f - interp) / 0.5f;
		float b = Mathf.Max((0.5f - interp) / 0.5f, 0.0f);

		return new Color(r, g, b);
	}

	private Color MonochromeHeat(float interp) {
		// Allows scaling of monochrome spectrum.
		float maxIntensity = 0.5f;
		float minIntensity = 0.0f;
		float w = interp * maxIntensity + minIntensity;

		return new Color(w, w, w);
	}

	private void UpdateMainCamera() {
		const float ORTHOGRAPHIC_SIZE_MIN = 0.1f;
		const float ORTHOGRAPHIC_SIZE_MAX = 0.5f;

		// Clamp zoom levels.
		Camera.main.orthographicSize = Mathf.Clamp(Camera.main.orthographicSize, ORTHOGRAPHIC_SIZE_MIN,
			ORTHOGRAPHIC_SIZE_MAX);
	}
}