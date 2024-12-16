# Cy/an Random

Does Random stuff I wanted to have in the game:
 - Void Field portal is usable more than once
    - Monster inherit Items from previous Void Field stages
    - extra Void Field stages increase item drops and monster items
        - entering the stage will 'activate' 5 * (number of entries) mountain shrines the number of active ones influence item scaling [porter shows up in objective list, will try to fix later]
            - i like it this way because i have other mods that do stuff with shrines, im probably gonna make this optional later (when adding properSave/riskofOptions funcionality)
        - items can still only be selected once per player but will drop multiplied after selection
        - Additional Items might fall of the edge. This is Intended. (in so far that i can't be bother to make it so that the items don't explode)
 - config with RiskOfOptions (this is optional, the mod will automatically use it if available and will still be usable with default values without)

 Currently just ideas:
 
 - void field stuff
    - might add a option to get all three items via round robin
- save so properSave can reload
- Remove all Highlights (maybe a button in the Settings?) from new unlocks

bugs/untested:
- variables persist over new games (RoR2.Run.Start?)
- artifact of evolution
- null portal does not sync to non-host (maybe fixed, can't test right now)