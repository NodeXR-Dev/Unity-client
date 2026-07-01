/*
 * File: NodeType.cs
 * Author: Developer 3
 * Created: 2026-05-20
 * Purpose: Defines node types for NodeXR graph.
 * Notes:
 * - UNKNOWN is used as a safe fallback when the server returns an unexpected type string.
 */

public enum NodeType { UNKNOWN, PART, PROPERTY, REFERENCE }
