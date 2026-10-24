package pt.yaku.booru.hydrus;

import jakarta.ws.rs.GET;
import jakarta.ws.rs.HeaderParam;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.QueryParam;

@Path("/")
public interface HydrusApi {

    @GET
    @Path("get_files/search_files")
    String searchFiles(@HeaderParam("Hydrus-Client-API-Access-Key") String accessKey,
                       @QueryParam("tags") String tagsJson);

    @GET
    @Path("get_files/file_metadata")
    String fileMetadata(@HeaderParam("Hydrus-Client-API-Access-Key") String accessKey,
                        @QueryParam("file_ids") String fileIdsJson);
}
