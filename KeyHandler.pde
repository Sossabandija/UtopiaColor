class KeyHandler{
  int[] KEYS = {65,87,68,83,32,   16,10,67,89,84,   85,38,40};
  int LEN = KEYS.length;
  boolean[] keysDown = new boolean[LEN];
  boolean[] keysToggle = new boolean[LEN];
  int[] keysPressTime = new int[LEN];
  public KeyHandler(){
    for(int i = 0; i < LEN; i++){
      keysDown[i] = false;
      keysToggle[i] = false;
      keysPressTime[i] = -9999999;
    }
  }
  void handle(int k, boolean state){
    for(int i = 0; i < LEN; i++){
      if(KEYS[i] != k){
        continue;
      }
      keysDown[i] = state;
      if(!state){ // everything after this clause only returns when a key is PRESSED, not LIFTED.
        continue;
      }
      keysToggle[i] = !keysToggle[i];
      keysPressTime[i] = millis();
      if(i == 6){ // ENTER to toggle mouse lock
        TRAP_MOUSE = !TRAP_MOUSE;
        r.confinePointer(TRAP_MOUSE);
        r.setPointerVisible(!TRAP_MOUSE);
      }
      if(i == 7){
        followSpecimen = !followSpecimen;
      }
      if(i == 9 && followSpecimen){
        if(closest_AI.target != null && getSpeciesType(closest_AI.target.species) >= 1){
          closest_AI = closest_AI.target;
        }
      }
      if(i == 11){
        map.renderSpeed = min(map.renderSpeed+1, 100);
      }
      if(i == 12){
        map.renderSpeed = max(map.renderSpeed-1, 1);
      }
    }
  }
  boolean keysToAction(int n){
    if(n <= 4){ // WASD and space
      return keysDown[n];
    }else if(n == 7){ // wandering
      return keysDown[8];
    }
    return false;
  }
}
