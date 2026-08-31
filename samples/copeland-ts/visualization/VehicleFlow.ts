flow VehicleFlow -> number {
    board {
        speed: number = 0;
        brakeEngaged: boolean = false;
        impactDetected: boolean = false;
    }

    event Start(speed: number);
    event Stop(speed: number);
    event Brake(engaged: boolean);
    event Impact(detected: boolean);
    event Wait();

    state Still initial {
        on Start(speed) when speed > 0 -> Moving {
            board.speed = speed;
        };
        on Wait() when board.speed == 0 -> Still;
    }

    state Moving {
        on Stop(speed) when speed == 0 -> Still {
            board.speed = speed;
        };
        on Brake(engaged) when engaged == true -> Still {
            board.brakeEngaged = engaged;
        };
        on Impact(detected) when detected == true -> Crash {
            board.impactDetected = detected;
        };
    }

    state Crash {
        finish board.speed;
    }
}
