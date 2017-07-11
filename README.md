# Nova Tellus

## Introduction

This is a map generator based on Voronoi diagrams.

Currently, only the elevation generation works, but additional functionality
such as erosion, hydrological simulation, and weather simulation is intended.
The elevation generation is based on a very simple and heavily fudged
simulation of plate tectonic activity.

![False Color Heightmap](https://github.com/loandy/novatellus/blob/screenshots/screenshots/screenshot001.png)

## Dependencies

I use a [modified version of PouletFrit's csDelaunay library](https://github.com/loandy/csDelaunay).
