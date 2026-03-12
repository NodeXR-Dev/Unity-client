using System;
using System.Collections.Generic;

namespace NodeXR
{
    public enum UnityPhase
    {
        BASIC_DISCUSS,        
        CORE_CANDIDATES,      
        CORE_CONFIRMED,       
        CATEGORY_SELECT,      
        CATEGORY_DISCUSS,     
        CATEGORY_CANDIDATES,  
        PREVIEW_3D
    }

    public enum GraphEventType
    {
        UNKNOWN,
        NODE_KEYWORD_UPDATE,
        NODE_IMAGE_UPDATE,
        CORE_IMAGE_UPDATE
    }

    [Serializable]
    public class GraphEventDto
    {
        public string @event; 
        public string root_label; // 서버의 "root_label"과 매핑
        public string core_img_url;
        public List<string> categories = new List<string>(); 
        public GraphStateDto graph_state;
    }

    [Serializable] 
    public class GraphStateDto 
    { 
        public string graph_snapshot_id; 
        public List<NodeDto> nodes = new List<NodeDto>(); 
        public List<EdgeDto> edges = new List<EdgeDto>(); 
    }
    
    [Serializable] 
    public class NodeDto 
    { 
        public string node_id;
        public string node_type;
        public string label;
        public string img_url;
    }

    [Serializable] 
    public class EdgeDto 
    { 
        public string edge_id;
        public string from_node_id;
        public string to_node_id; 
    }
}