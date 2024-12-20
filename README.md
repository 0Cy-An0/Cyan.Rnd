# Cy/an Random

Does Random stuff I wanted to have in the game:
 - Remove all Highlights from new unlocks (this is a option in the Settings)
 - Void Field portal is usable more than once
    - Monster inherit Items from previous Void Field stages
    - extra Void Field stages increase item drops and monster items
        - all things listed below are further configurable via the config (or RiskOfOptions)
        - entering the stage will 'activate' mountain shrines the number of active ones influence item scaling [porter shows up in objective list, will try to fix later]
            - i like it this way because i have other mods that do stuff with shrines, im probably gonna make this optional later
        - reward items can still only be selected once per player but will drop multiplied after selection
            - Additional Items might fall of the edge. This is Intended. (in so far that i can't be bother to make it so that the items don't explode)
        - there is a option to get more monsters instead of just 1 type added
 - config with RiskOfOptions (this is optional, the mod will automatically use it if available. Otherwise you have to set the config yourself)
 - saves with ProperSave (this too is optional)

 Currently just ideas:
 
 - void field stuff
    - might add a option to get all three items via round robin
    - mountain shrine optional
    - exponential scaling option

bugs/untested:
- artifact of evolution
- null portal does not sync to non-host (maybe fixed, can't test right now)
- what happens with other orbs (like the void field rewards, there is currently no check were you are so if you get them somehwere else it should also give multiplied rewards)
- adding more than 5 monster types breaks. (it seems like there are only 5 directors and every new monster should get its own one)