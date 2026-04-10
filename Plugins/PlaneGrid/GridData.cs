using MissionPlanner.Utilities;
using System.Collections.Generic;

namespace PlaneGrid
{
    public struct PlaneGridData
    {
        public List<PointLatLngAlt> poly;
        //simple
        public string camera;
        public decimal alt;
        public decimal angle;
        public decimal speed;

        //options
        public decimal dist;
        public decimal overshoot1;
        public decimal overshoot2;
        public decimal leadin;
        public string startfrom;
        public decimal overlap;
        public decimal sidelap;
        public decimal spacing;
        public bool crossgrid;

        // plane settings
        public decimal minlaneseparation;

        public bool breaktrigdist;
    }
}