# MoarCamz

Spiritual successor to Joan's old Camera Editor plugin.

* Add more than 10 cameras (hit the + and - buttons to the left of the camera bar to add and remove extra cams, drag left/right to scroll to them).
* More camera control options, direct setting entry and fast/slow drag pads.
* Center a camera on a target of your choice
* More feedback on camera preset state via colors
  - Light Grey - Not set - still on default
  - White - Set
  - Green - Matches current camera setting
  - Blue - Previous selection but camera setting does not match. Basically, you selected this and then moved the camera - for people like me with the memory of a goldfish trying to fix a cam and then forgetting which one I was editing.
* Next/Previous Camera Buttons (Default numpad +/-) - skips over unset cameras

Rebind hotkeys and set the drag pads sensitivity in F1->Plugin Settings.

### Camera Centering:

You can center a camera by selecting the center object desired and clicking set on the interface. The selection is basically whatever is selected for purposes of the bottom left pos/rot bar. Can be an Object from the menu or an IK/FK guide object.
Toggle the centering active by toggling on either of the two lock on targets. This causes the camera to center at the position of the object in question. Moving the camera now provides an offset from that location. Rotation and distance orbit around that as normal.

## UI Guide
![UI Guide Image](https://raw.githubusercontent.com/OrangeSpork/MoarCamz/master/MoarCamz/Guide.png)
