using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using csDelaunay;

/**
 * Singleton.
 */
public class Selection {
    private const float EPSILON = 0.005f;
    private List<LR> edgeOrientations;
    private List<Edge> edges;
    private List<Vector2f> region;

    public HashSet<Cell> SelectedCells { get; set; }
    public HashSet<Cell> BoundaryCells { get; set; }

    public Selection() {
        this.SelectedCells = new HashSet<Cell>();
        this.BoundaryCells = new HashSet<Cell>();
        this.edgeOrientations = new List<LR>();
        this.edges = new List<Edge>();
    }

    /**
     * Adds a cell to the selection.
     */
    public void AddCell(Cell cell) {
        // Add the selected cell to selection.
        this.SelectedCells.Add(cell);

        // If the selected cell was on the boundary, remove from boundary.
        if (this.BoundaryCells.Contains(cell)) {
            this.BoundaryCells.Remove(cell);
        }

        // Add the selected cell's neighbors to boundary.
        foreach (KeyValuePair<Site, Edge> kv in cell.Site.NeighborSiteEdges()) {
            Site neighborSite = kv.Key;
            Edge neighborEdge = kv.Value;
            Cell neighborCell = Cell.SiteCells[neighborSite];
            this.BoundaryCells.Add(neighborCell);
        }
    }

    public void RemoveCell(Cell cell) {
        // Remove cell from selection.
        this.SelectedCells.Remove(cell);

        // If any cells remain selected, the newly deselected cell should be on the boundary now.
        if (this.SelectedCells.Any()) {
            this.BoundaryCells.Add(cell);
        }

        // Check all neighboring cells to determine which should be removed from the boundary.
        foreach (Site neighborSite in cell.Site.NeighborSites()) {
            Cell neighborCell = Cell.SiteCells[neighborSite];

            bool boundarySite = false;
            foreach (Site site in neighborCell.Site.NeighborSites()) {
                if (this.SelectedCells.Contains(Cell.SiteCells[site])) {
                    boundarySite = true;
                }
            }

            if (!boundarySite) {
                this.BoundaryCells.Remove(neighborCell);
            }
        }
    }

    public void Clear() {
        this.SelectedCells.Clear();
        this.BoundaryCells.Clear();
        this.edgeOrientations.Clear();
        this.edges.Clear();
    }

    public List<Vector2f> Region(Rectf clippingBounds) {
        if (this.edges == null || this.edges.Count == 0) {
            return new List<Vector2f>();
        }

        if (this.edgeOrientations == null) {
            this.ReorderEdges();
            this.region = this.ClipToBounds(clippingBounds);
            if ((new Polygon(this.region)).PolyWinding == Winding.CLOCKWISE) {
                this.region.Reverse();
            }
        }

        return this.region;
    }

    private static bool CloseEnough(Vector2f p0, Vector2f p1) {
        return (p0 - p1).Magnitude < EPSILON;
    }

    private void ReorderEdges() {
        EdgeReorderer reorderer = new EdgeReorderer(edges, typeof(Vertex));
        this.edges = reorderer.Edges;
        this.edgeOrientations = reorderer.EdgeOrientations;
        reorderer.Dispose();
    }

    private List<Vector2f> ClipToBounds(Rectf bounds) {
        List<Vector2f> points = new List<Vector2f>();
        int n = this.edges.Count;
        int i = 0;
        Edge edge;

        // Linear search for an initial visible edge.
        while (i < n && !this.edges[i].Visible) {
            i++;
        }

        // Reached end of edge list without encountering a visible edge.
        if (i == n) {
            // No edges visible
            return new List<Vector2f>();
        }

        edge = this.edges[i];
        LR orientation = this.edgeOrientations[i];
        points.Add(edge.ClippedVertices[orientation]);
        points.Add(edge.ClippedVertices[LR.Other(orientation)]);

        // Continue to process any additional visible edges.
        for (int j = i + 1; j < n; j++) {
            edge = this.edges[j];
            if (!edge.Visible) {
                continue;
            }
            this.Connect(ref points, j, bounds);
        }
        // Close up the polygon by adding another corner point of the bounds if needed:
        this.Connect(ref points, i, bounds, true);

        return points;
    }

    private void Connect(ref List<Vector2f> points, int j, Rectf bounds, bool closingUp = false) {
        Vector2f rightPoint = points[points.Count - 1];
        Edge newEdge = this.edges[j];
        LR newOrientation = this.edgeOrientations[j];

        // The point that must be conected to rightPoint:
        Vector2f newPoint = newEdge.ClippedVertices[newOrientation];

        if (!Selection.CloseEnough(rightPoint, newPoint)) {
            // The points do not coincide, so they must have been clipped at the bounds;
            // see if they are on the same border of the bounds:
            if (rightPoint.x != newPoint.x && rightPoint.y != newPoint.y) {
                // They are on different borders of the bounds;
                // insert one or two corners of bounds as needed to hook them up:
                // (NOTE this will not be correct if the region should take up more than
                // half of the bounds rect, for then we will have gone the wrong way
                // around the bounds and included the smaller part rather than the larger)
                int rightCheck = BoundsCheck.Check(rightPoint, bounds);
                int newCheck = BoundsCheck.Check(newPoint, bounds);
                float px, py;
                if ((rightCheck & BoundsCheck.RIGHT) != 0) {
                    px = bounds.Right;

                    if ((newCheck & BoundsCheck.BOTTOM) != 0) {
                        py = bounds.Bottom;
                        points.Add(new Vector2f(px, py));

                    } else if ((newCheck & BoundsCheck.TOP) != 0) {
                        py = bounds.Top;
                        points.Add(new Vector2f(px, py));

                    } else if ((newCheck & BoundsCheck.LEFT) != 0) {
                        if (rightPoint.y - bounds.Y + newPoint.y - bounds.Y < bounds.Height) {
                            py = bounds.Top;
                        } else {
                            py = bounds.Bottom;
                        }
                        points.Add(new Vector2f(px, py));
                        points.Add(new Vector2f(bounds.Left, py));
                    }
                } else if ((rightCheck & BoundsCheck.LEFT) != 0) {
                    px = bounds.Left;

                    if ((newCheck & BoundsCheck.BOTTOM) != 0) {
                        py = bounds.Bottom;
                        points.Add(new Vector2f(px,py));

                    } else if ((newCheck & BoundsCheck.TOP) != 0) {
                        py = bounds.Top;
                        points.Add(new Vector2f(px,py));

                    } else if ((newCheck & BoundsCheck.RIGHT) != 0) {
                        if (rightPoint.y - bounds.Y + newPoint.y - bounds.Y < bounds.Height) {
                            py = bounds.Top;
                        } else {
                            py = bounds.Bottom;
                        }
                        points.Add(new Vector2f(px, py));
                        points.Add(new Vector2f(bounds.Right, py));
                    }
                } else if ((rightCheck & BoundsCheck.TOP) != 0) {
                    py = bounds.Top;

                    if ((newCheck & BoundsCheck.RIGHT) != 0) {
                        px = bounds.Right;
                        points.Add(new Vector2f(px, py));

                    } else if ((newCheck & BoundsCheck.LEFT) != 0) {
                        px = bounds.Left;
                        points.Add(new Vector2f(px, py));

                    } else if ((newCheck & BoundsCheck.BOTTOM) != 0) {
                        if (rightPoint.x - bounds.X + newPoint.x - bounds.X < bounds.Width) {
                            px = bounds.Left;
                        } else {
                            px = bounds.Right;
                        }
                        points.Add(new Vector2f(px, py));
                        points.Add(new Vector2f(px, bounds.Bottom));
                    }
                } else if ((rightCheck & BoundsCheck.BOTTOM) != 0) {
                    py = bounds.Bottom;

                    if ((newCheck & BoundsCheck.RIGHT) != 0) {
                        px = bounds.Right;
                        points.Add(new Vector2f(px, py));

                    } else if ((newCheck & BoundsCheck.LEFT) != 0) {
                        px = bounds.Left;
                        points.Add(new Vector2f(px, py));

                    } else if ((newCheck & BoundsCheck.TOP) != 0) {
                        if (rightPoint.x - bounds.X + newPoint.x - bounds.X < bounds.Width) {
                            px = bounds.Left;
                        } else {
                            px = bounds.Right;
                        }
                        points.Add(new Vector2f(px, py));
                        points.Add(new Vector2f(px, bounds.Top));
                    }
                }
            }
            if (closingUp) {
                // newEdge's ends have already been added
                return;
            }
            points.Add(newPoint);
        }
        Vector2f newRightPoint = newEdge.ClippedVertices[LR.Other(newOrientation)];
        if (!Selection.CloseEnough(points[0], newRightPoint)) {
            points.Add(newRightPoint);
        }
    }
}

public class Cell : MonoBehaviour {
    private static Selection selection = null;
    private static VoronoiDiagram map;
    private static Color highlightColor = Color.magenta;
    private static Stopwatch clickTimer = new Stopwatch();
    private Color originalColor;
    private Vector3 screenPoint;
    private Vector3 offset;
    private Renderer rend;

    // Global dictionary of cells looked up by site.
    public static Dictionary<Site, Cell> SiteCells { get; private set; }
    public Site Site { get; set; }

    static Cell() {
        Cell.SiteCells = new Dictionary<Site, Cell>();
    }

    public static Selection GetSelection() {
        if (Cell.selection == null) {
            Cell.selection = new Selection();
        }
        return Cell.selection;
    }

    public void SetSite(Site site) {
        this.Site = site;
        Cell.SiteCells[site] = gameObject.GetComponent(typeof(Cell)) as Cell;
    }

    public bool IsSelected() {
        Cell cellObject = this.gameObject.GetComponent(typeof(Cell)) as Cell;
        return Cell.selection.SelectedCells.Contains(cellObject);
    }

    // Use this for initialization.
    void Awake() {
        this.Site = null;

        if (Cell.map == null) {
            GameObject go = GameObject.Find("Map");
            Cell.map = go.GetComponent<VoronoiDiagram>();
        }

        this.rend = gameObject.GetComponent<Renderer>();
    }

    void Start () {

    }

    // Update is called once per frame
    void Update () {

    }

    void OnMouseEnter() {
        this.originalColor = this.rend.material.color;
        this.rend.material.color = Cell.highlightColor;
    }

    void OnMouseExit() {
        this.rend.material.color = this.originalColor;
    }

    void OnMouseDown() {
        Cell.clickTimer.Reset();
        Cell.clickTimer.Start();

        // Get offset for each cell in the selection.
        Selection selection = Cell.GetSelection();
        foreach (Cell cell in selection.SelectedCells) {
            cell.screenPoint = Camera.main.WorldToScreenPoint(cell.transform.position);
            Vector3 inputPoint = new Vector3(Input.mousePosition.x, Input.mousePosition.y,
                cell.screenPoint.z);
            Vector3 worldPoint = Camera.main.ScreenToWorldPoint(inputPoint);
            cell.offset = cell.transform.position - worldPoint;
        }
    }

    void OnMouseUp() {
        Cell.clickTimer.Stop();

        // If slow click, ignore.
        if (Cell.clickTimer.ElapsedMilliseconds > 300) {
            return;
        }

        if (Input.GetMouseButtonUp(0)) {
            Cell cellObject = this.gameObject.GetComponent(typeof(Cell)) as Cell;
            Selection selection = Cell.GetSelection();

            if (!this.IsSelected()) {
                // Only add the cell to the existing selection if it is contiguous with the existing selection.
                // Otherwise, create a new selection.
                if (selection.BoundaryCells.Contains(cellObject)) {
                    selection.AddCell(cellObject);
                } else {
                    selection.Clear();
                    selection.AddCell(cellObject);
                }

                this.originalColor = this.rend.material.color;
                this.rend.material.color = Cell.highlightColor;
            } else {
                selection.RemoveCell(cellObject);
                this.rend.material.color = this.originalColor;
            }

            UnityEngine.Debug.Log(selection.SelectedCells.Count + " selected cells.");
        }
    }

    void OnMouseDrag() {
        if (Cell.clickTimer.ElapsedMilliseconds < 300) {
            return;
        }
        //Cell.clickTimer.Stop();

        UnityEngine.Debug.Log("OnMouseDrag fired.");
        foreach (Cell cell in Cell.selection.SelectedCells) {
            Vector3 curScreenPoint = new Vector3(Input.mousePosition.x, Input.mousePosition.y,
                cell.screenPoint.z);
            Vector3 curPosition = Camera.main.ScreenToWorldPoint(curScreenPoint) + cell.offset;

            // Bind mouse cursor to within the map bounds.
            if (curPosition.x > Cell.map.BoundOffsetX) {
                curPosition.x = Cell.map.BoundOffsetX;
            } else if (curPosition.x < -Cell.map.BoundOffsetX) {
                curPosition.x = -Cell.map.BoundOffsetX;
            }
            if (curPosition.y > Cell.map.BoundOffsetY) {
                curPosition.y = Cell.map.BoundOffsetY;
            } else if (curPosition.y < -Cell.map.BoundOffsetY) {
                curPosition.y = -Cell.map.BoundOffsetY;
            }
            cell.transform.position = curPosition;

            cell.Site.X = curPosition.x * map.Width + map.BoundOffsetX * map.Width;
            cell.Site.Y = curPosition.y * map.Height + map.BoundOffsetY * map.Height;
        }

        map.RefreshSections();
        map.ClearScreen();
        map.Render();
    }
}