package pt.yaku.booru.dapi;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;

@Getter
@Setter
@NoArgsConstructor
public class DapiTag {
    private long id;
    private String name;
    private long count;
    private int type;
    private int ambiguous;
}
