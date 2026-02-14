type
++++

:default: ``MustNot``

This attribute determines if this is an inclusive or exclusive filter rule. 

``Must``
    Inclusive filters reduce the set of monitored resources to include only those that match at least one of the filters.

``MustNot``
    Exclusive filters reduce the set of monitored resources by those that match with them.