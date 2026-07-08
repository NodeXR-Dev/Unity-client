/*
 * File: EdgeData.cs
 * Author: Developer 3
 * Created: 2026-05-20
 * Purpose: Defines graph edge data for NodeXR.
 * Notes:
 * - Edge type is intentionally omitted in MVP.
 * - Relationship is inferred from from/to node types.
 * - PROPERTY → PROPERTY means property refinement.
 * - PROPERTY → PART means applying a property to a part or ALL.
 * - REFERENCE → PROPERTY means the reference describes a property.
 * - REFERENCE → PART means the reference applies to a part or ALL.
 */

[System.Serializable]
public class EdgeData
{
    public string edge_id;
    public string from_node_id;
    public string to_node_id;
}
