using Flux.API;

Console.WriteLine ("Hello, World!");
Point3 ps = new (39.33026, -23.696316, 6);
Point3 pe = new (41.124299, -26.224453, 6);
Point3 ip1 = new (38.64852698, -22.91992291, 6);
Point3 ip2 = new (37.92760784, -22.1797731, 6);
Arc3 arc = new (ps, ip1, ip2, pe);
var p1 = arc.Evaluate (0.3);
var p2 = arc.Evaluate (0.7);
Console.WriteLine ($"Point at param {0.3} is {p1}");
Console.WriteLine ($"Point at param {0.7} is {p2}");
