using UnityEngine;
using System.Collections;
using System.Collections.Generic;

using csDelaunay;

public class Cell : MonoBehaviour {
	public Site Site;
	static private VoronoiDiagram map;
	static private Color highlightColor = Color.red;
	private Color originalColor;
	private Vector3 screenPoint;
	private Vector3 offset;

	// Use this for initialization
	void Start () {
		if (Cell.map == null) {
			GameObject go = GameObject.Find("Map");
			Cell.map = go.GetComponent<VoronoiDiagram>();
		}
	}

	// Update is called once per frame
	void Update () {

	}

	void OnMouseEnter() {
		Renderer renderer = gameObject.GetComponent<Renderer>();
		this.originalColor = renderer.material.color;
		renderer.material.color = Cell.highlightColor;
	}

	void OnMouseExit() {
		Renderer renderer = gameObject.GetComponent<Renderer>();
		renderer.material.color = this.originalColor;
	}

	void OnMouseDown() {
		this.screenPoint = Camera.main.WorldToScreenPoint(transform.position);
		Vector3 inputPoint = new Vector3(Input.mousePosition.x, Input.mousePosition.y,
			this.screenPoint.z);
		Vector3 worldPoint = Camera.main.ScreenToWorldPoint(inputPoint);
		this.offset = transform.position - worldPoint;
	}

	void OnMouseDrag() {
		Vector3 curScreenPoint = new Vector3(Input.mousePosition.x, Input.mousePosition.y,
			this.screenPoint.z);
		Vector3 curPosition = Camera.main.ScreenToWorldPoint(curScreenPoint) + this.offset;

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
		transform.position = curPosition;

		this.Site.X = curPosition.x * map.Width + map.BoundOffsetX * map.Width;
		this.Site.Y = curPosition.y * map.Height + map.BoundOffsetY * map.Height;
		map.RefreshSections();
		map.Render();
	}
}