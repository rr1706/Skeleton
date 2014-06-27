RatchetSkeleton
 by Team 1706, the Ratchet Rockers.

This program allows for robot control based on hand movements. It allows for
control of the motors and for special actions, such as launching a ball and
retrieving a ball. The program sends the processed data as a UDP message to the
configured IP address.

The UDP output of this application can be parsed with the following format
string:
%f %f %d
The first parameter is the motor speed X.
The second parameter is the motor speed Y.
The third parameter is an int. 0 means do nothing, 1 means fire, and 2 means do
intake.