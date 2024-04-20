using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tileman
{
    class ModData

    {
        public bool ToPlaceTiles { get; set; }
        public bool DoCollision { get; set; }  
        
        public bool AllowPlayerPlacement { get; set; }
        public bool ToggleOverlay { get; set; }

        public double TilePrice { get; set; }
        public double TilePriceRaise { get; set; }

        public int CavernsExtra { get; set; }
        public int DifficultyMode { get; set; } 

        public int PurchaseCount { get; set; }

        public bool Legacy { get; set; } = true;

        public int LocationTransitionTime { get; set; } = 3;

        public int OverlayMode { get; set; } = 2;

    }
}
