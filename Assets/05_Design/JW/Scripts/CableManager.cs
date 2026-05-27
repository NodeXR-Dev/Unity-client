using UnityEngine;
using System.Collections.Generic;

public class CableManager : MonoBehaviour
{
    public static CableManager Instance;
    public GameObject cablePrefab;

    private Dictionary<string, GameObject> activeConnections = new();

    void Awake() => Instance = this;

    public void OnConnection(ConnectorEndpoint a, ConnectorEndpoint b)
        string key = GetKey(a, b);
        if (activeConnections.ContainsKey(key)) return;

        var cable = Instantiate(cablePrefab);
        var cr = cable.GetComponent<CableRenderer>();
        cr.SetEndpoints(a.transform, b.transform);

        activeConnections[key] = cable;
    }

    public void OnDisconnection(ConnectorEndpoint a)
    {
        foreach (var key in new List<string>(activeConnections.Keys))
        {
            if (key.Contains(a.GetInstanceID().ToString()))
            {
                Destroy(activeConnections[key]);
                activeConnections.Remove(key);
            }
        }
    }

    string GetKey(ConnectorEndpoint a, ConnectorEndpoint b)
        => $"{Mathf.Min(a.GetInstanceID(), b.GetInstanceID())}" +
           $"_{Mathf.Max(a.GetInstanceID(), b.GetInstanceID())}";
}